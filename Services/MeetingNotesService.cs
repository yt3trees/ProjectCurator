using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ProjectCurator.Models;

namespace ProjectCurator.Services;

/// <summary>
/// 会議メモを解析し、decision_log / current_focus.md / open_issues.md への反映を管理するサービス。
/// </summary>
public class MeetingNotesService
{
    private readonly LlmClientService        _llm;
    private readonly ConfigService           _config;
    private readonly FileEncodingService     _encoding;
    private readonly ProjectDiscoveryService _discovery;
    private readonly AsanaTaskParser         _asanaParser;
    private readonly CaptureService          _capture;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy        = JsonNamingPolicy.SnakeCaseLower,
    };

    // open_issues.md のセクション見出し (日本語優先 → 英語フォールバック)
    private static readonly (string Key, string[] Candidates)[] OpenIssueSections =
    [
        ("open_questions", ["## Open questions", "## オープンクエスチョン"]),
        ("concerns",       ["## Risks and concerns", "## Concerns", "## Risks"]),
    ];

    // current_focus.md のセクション見出し
    private const string SectionRecentContext = "## 最近あったこと";
    private const string SectionNextActions   = "## 次やること";

    public MeetingNotesService(
        LlmClientService        llm,
        ConfigService           config,
        FileEncodingService     encoding,
        ProjectDiscoveryService discovery,
        AsanaTaskParser         asanaParser,
        CaptureService          capture)
    {
        _llm         = llm;
        _config      = config;
        _encoding    = encoding;
        _discovery   = discovery;
        _asanaParser = asanaParser;
        _capture     = capture;
    }

    // =========================================================================
    // 1. 分析: 会議メモ → 構造化結果
    // =========================================================================
    public async Task<MeetingAnalysisResult> AnalyzeAsync(
        string meetingNotes,
        ProjectInfo project,
        string? workstreamId,
        CancellationToken ct)
    {
        var focusPath     = ResolveFocusPath(project, workstreamId);
        var openIssuesPath  = ResolveOpenIssuesPath(project, workstreamId);
        var asanaPath     = ResolveAsanaTasksPath(project, workstreamId);

        var currentFocus    = "";
        var currentOpenIssues = "";

        if (File.Exists(focusPath))
            (currentFocus, _) = await _encoding.ReadFileAsync(focusPath, ct);
        if (File.Exists(openIssuesPath))
            (currentOpenIssues, _) = await _encoding.ReadFileAsync(openIssuesPath, ct);

        var currentAsanaSummary = "";
        if (File.Exists(asanaPath))
        {
            var (asanaContent, _) = await _encoding.ReadFileAsync(asanaPath, ct);
            var parsed = _asanaParser.Parse(asanaContent);
            currentAsanaSummary = FormatAsanaTasksForPrompt(parsed);
        }

        var systemPrompt = BuildSystemPrompt();
        var userPrompt   = BuildUserPrompt(meetingNotes, project.Name, workstreamId, currentFocus, currentOpenIssues, currentAsanaSummary);

        string raw;
        try
        {
            raw = await _llm.ChatCompletionAsync(systemPrompt, userPrompt, ct);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MeetingNotesService] LLM error: {ex.Message}");
            throw;
        }

        var result = ParseResult(raw);
        result.DebugSystemPrompt = _llm.LastSystemPrompt; // User Profile 注入後の実際のプロンプト
        result.DebugUserPrompt   = userPrompt;
        result.DebugResponse     = raw;

        // FocusUpdate の ProposedContent を確定
        // LLM が proposed_content を出力した場合はそれを優先、なければ文字列挿入でフォールバック
        if (result.FocusUpdate.RecentContext.Count > 0 || result.FocusUpdate.NextActions.Count > 0)
        {
            result.FocusUpdate.CurrentContent = currentFocus;
            if (string.IsNullOrWhiteSpace(result.FocusUpdate.ProposedContent))
                result.FocusUpdate.ProposedContent = BuildFocusProposed(currentFocus, result.FocusUpdate);
        }

        // Open Issues の AppendContent を組み立て
        if (result.Tensions.HasItems)
        {
            result.Tensions.CurrentContent = currentOpenIssues;
            result.Tensions.AppendContent  = BuildOpenIssuesAppend(result.Tensions);
        }

        // AsanaTasks の AppendContent を組み立て
        if (result.AsanaTasks.HasItems)
            result.AsanaTasks.AppendContent = BuildAsanaAppend(result.AsanaTasks);

        return result;
    }

    // =========================================================================
    // 2. Decision Log 作成
    // =========================================================================
    public async Task<List<string>> ApplyDecisionsAsync(
        MeetingAnalysisResult result,
        ProjectInfo project,
        string? workstreamId)
    {
        var created = new List<string>();
        var logDir  = ResolveDecisionLogDir(project, workstreamId);
        Directory.CreateDirectory(logDir);

        foreach (var decision in result.Decisions.Where(d => d.IsSelected))
        {
            var topic        = SanitizeTopic(decision.FilenameTopic);
            var baseFileName = $"{DateTime.Now:yyyy-MM-dd}_{topic}.md";
            var filePath     = GetUniqueDecisionLogPath(logDir, baseFileName);
            var content      = BuildDecisionLogContent(decision);
            await _encoding.WriteFileAsync(filePath, content, "UTF8");
            created.Add(filePath);
        }

        return created;
    }

    // =========================================================================
    // 3. current_focus.md 更新
    // =========================================================================
    public async Task<string?> ApplyFocusAsync(
        MeetingAnalysisResult result,
        ProjectInfo project,
        string? workstreamId)
    {
        if (!result.FocusUpdate.IsSelected) return null;
        if (string.IsNullOrWhiteSpace(result.FocusUpdate.ProposedContent)) return null;

        var focusPath = ResolveFocusPath(project, workstreamId);
        if (!File.Exists(focusPath)) return null;

        // バックアップ (FocusUpdateService と同パターン)
        await CreateFocusBackupAsync(focusPath);

        var (_, enc) = await _encoding.ReadFileAsync(focusPath);
        await _encoding.WriteFileAsync(focusPath, result.FocusUpdate.ProposedContent, enc);
        return focusPath;
    }

    // =========================================================================
    // 4. open_issues.md 更新
    // =========================================================================
    public async Task<string?> ApplyOpenIssuesAsync(
        MeetingAnalysisResult result,
        ProjectInfo project,
        string? workstreamId)
    {
        if (!result.Tensions.IsSelected) return null;
        if (!result.Tensions.HasItems) return null;

        var openIssuesPath = ResolveOpenIssuesPath(project, workstreamId);

        string existingContent;
        string enc;

        if (File.Exists(openIssuesPath))
        {
            (existingContent, enc) = await _encoding.ReadFileAsync(openIssuesPath);
        }
        else
        {
            var dir = Path.GetDirectoryName(openIssuesPath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);
            existingContent = BuildNewOpenIssuesTemplate();
            enc = "utf-8";
        }

        var updated = ApplyOpenIssuesToContent(existingContent, result.Tensions);
        await _encoding.WriteFileAsync(openIssuesPath, updated, enc);
        return openIssuesPath;
    }

    // =========================================================================
    // 5. Asana API 起票 + tasks.md 追記
    // =========================================================================
    public async Task<MeetingAsanaApplyResult> ApplyAsanaTasksAsync(
        MeetingAnalysisResult result,
        ProjectInfo project,
        string? workstreamId,
        CancellationToken ct)
    {
        var applyResult = new MeetingAsanaApplyResult();
        if (!result.AsanaTasks.IsSelected) return applyResult;
        var selected = result.AsanaTasks.Tasks.Where(t => t.IsSelected).ToList();
        if (selected.Count == 0) return applyResult;

        // tasks.md の読み込み
        var asanaPath = ResolveAsanaTasksPath(project, workstreamId);
        string existing = "";
        string enc      = "utf-8";
        if (File.Exists(asanaPath))
            (existing, enc) = await _encoding.ReadFileAsync(asanaPath);

        var sb = new StringBuilder(existing.TrimEnd());
        foreach (var task in selected)
        {
            var priorityTag = task.Priority is "High" or "Medium" or "Low"
                ? $" [{task.Priority}]" : "";
            string? createdGid = null;

            // タスクごとの DueAt を組み立て (日時両方あれば ISO 8601、日付のみなら DueOn)
            var dueOn = task.DueOn;
            var dueAt = "";
            if (!string.IsNullOrWhiteSpace(task.DueOn) && !string.IsNullOrWhiteSpace(task.DueTime))
            {
                var offset = TimeZoneInfo.Local.GetUtcOffset(DateTime.Now);
                var sign   = offset >= TimeSpan.Zero ? "+" : "-";
                dueAt = $"{task.DueOn}T{task.DueTime}:00.000{sign}{Math.Abs(offset.Hours):D2}:{Math.Abs(offset.Minutes):D2}";
                dueOn = "";  // DueAt 指定時は DueOn 不要
            }

            // Asana API 起票 (タスクごとのプロジェクトが選択された場合のみ)
            if (!string.IsNullOrWhiteSpace(task.ProjectGid))
            {
                var preview = new AsanaTaskCreatePreview
                {
                    ProjectGid  = task.ProjectGid,
                    ProjectName = task.ProjectName,
                    SectionGid  = task.SectionGid,
                    SectionName = task.SectionName,
                    TaskName    = task.Title,
                    Notes       = task.Notes,
                    DueOn       = dueOn,
                    DueAt       = dueAt,
                };
                var iKey   = CaptureService.BuildIdempotencyKey(task.ProjectGid, task.Title, task.Notes);
                var apiRes = await _capture.CreateAsanaTaskAsync(preview, iKey, ct);
                if (apiRes.Success)
                {
                    applyResult.ApiSuccessCount++;
                    createdGid = apiRes.AsanaTaskGid;
                }
                else
                {
                    applyResult.ApiFailCount++;
                    applyResult.Errors.Add($"{task.Title}: {apiRes.Message}");
                }
            }

            // tasks.md への追記 (ID 付きで記録)
            var idTag  = !string.IsNullOrWhiteSpace(createdGid) ? $" [id:{createdGid}]" : "";
            var dueTag = !string.IsNullOrWhiteSpace(task.DueOn)
                ? $" (Due: {task.DueOn}{(!string.IsNullOrWhiteSpace(task.DueTime) ? " " + task.DueTime : "")})" : "";
            sb.AppendLine();
            sb.AppendLine($"- [ ] {task.Title}{priorityTag}{dueTag}{idTag}");
            if (!string.IsNullOrWhiteSpace(task.Notes))
                sb.AppendLine($"    > {task.Notes}");
        }

        await _encoding.WriteFileAsync(asanaPath, sb.ToString(), enc);
        applyResult.FilePath = asanaPath;
        return applyResult;
    }

    // =========================================================================
    // パス解決
    // =========================================================================
    private static string ResolveFocusPath(ProjectInfo project, string? workstreamId)
    {
        if (!string.IsNullOrWhiteSpace(workstreamId))
        {
            var wsPath = Path.Combine(
                project.AiContextContentPath, "workstreams", workstreamId, "current_focus.md");
            if (File.Exists(wsPath)) return wsPath;
        }
        return Path.Combine(project.AiContextContentPath, "current_focus.md");
    }

    private static string ResolveOpenIssuesPath(ProjectInfo project, string? workstreamId)
    {
        if (!string.IsNullOrWhiteSpace(workstreamId))
        {
            var wsPath = Path.Combine(
                project.AiContextContentPath, "workstreams", workstreamId, "open_issues.md");
            if (File.Exists(wsPath)) return wsPath;
        }
        return Path.Combine(project.AiContextContentPath, "open_issues.md");
    }

    private static string ResolveAsanaTasksPath(ProjectInfo project, string? workstreamId)
    {
        var obsidianNotes = Path.Combine(project.AiContextPath, "obsidian_notes");
        if (!string.IsNullOrWhiteSpace(workstreamId))
        {
            var wsPath = Path.Combine(obsidianNotes, "workstreams", workstreamId, "tasks.md");
            if (File.Exists(wsPath)) return wsPath;
        }
        return Path.Combine(obsidianNotes, "tasks.md");
    }

    private static string ResolveDecisionLogDir(ProjectInfo project, string? workstreamId)
    {
        if (!string.IsNullOrWhiteSpace(workstreamId))
        {
            var wsDir = Path.Combine(
                project.AiContextContentPath, "workstreams", workstreamId, "decision_log");
            return wsDir;
        }
        return Path.Combine(project.AiContextContentPath, "decision_log");
    }

    // =========================================================================
    // プロンプト
    // =========================================================================
    private static string BuildSystemPrompt() => """
        You are an assistant that analyzes meeting or session notes and categorizes information
        into three types for project context management.

        ## Output format
        Output ONLY a JSON object with exactly these four keys.

        {
          "decisions": [
            {
              "filename_topic": "english_snake_case",
              "title": "English Title",
              "status": "confirmed|tentative",
              "trigger": "meeting|ai_session|solo",
              "context": "2-3 sentences",
              "option_a_name": "Name",
              "option_a_pros": "...",
              "option_a_cons": "...",
              "option_b_name": "Name",
              "option_b_pros": "...",
              "option_b_cons": "...",
              "chosen": "Option A/B: Name",
              "why": "2-4 sentences",
              "risk": "...",
              "revisit_trigger": "measurable condition"
            }
          ],
          "focus_updates": {
            "recent_context": ["item 1", "item 2"],
            "next_actions": ["action 1", "action 2"],
            "proposed_content": "<full updated content of current_focus.md>"
          },
          "tensions": {
            "open_questions": ["question or trade-off 1"],
            "concerns": ["concern 1"]
          },
          "asana_tasks": [
            {
              "title": "Task title",
              "priority": "High|Medium|Low|",
              "notes": "optional brief context"
            }
          ]
        }

        ## Classification rules

        ### decisions[]
        - Record only when a real CHOICE was made between alternatives
        - If only one option was discussed (no real comparison), omit from decisions
        - Status "tentative" if explicitly described as provisional/temporary
        - filename_topic: always English snake_case (used for file naming)
        - Body text (title, context, options, etc.): follow the user's language preference
        - Options: infer the alternative that was NOT chosen if not explicitly stated
        - Minimum quality: option_a and option_b must be meaningfully different

        ### focus_updates
        - recent_context: summary list of what happened (used for display count only)
        - next_actions: summary list of next actions (used for display count only)
        - proposed_content: the COMPLETE updated content of current_focus.md, from the first line
          to the last. Integrate meeting outcomes naturally into the existing file.
          Rules for proposed_content:
          - PRESERVE the existing Markdown heading/section structure exactly (do not add, remove, or rename sections)
          - Keep user-written lines that are still relevant
          - Add new "最近あったこと" items from recent_context at the top of that section
          - Add new "次やること" items from next_actions at the top of that section
          - Do not duplicate items already present
          - Update the date line (更新: or Last Updated:) to today's date
          - Never truncate — always output the complete file
          - If current_focus.md is empty or "(none)", output an empty string ""
          - Output must be a valid JSON string (escape newlines as \\n, quotes as \\")

        ### tensions
        - open_questions: unresolved questions and trade-offs with no clear resolution yet
        - concerns: risks or inconsistencies to watch
        - Omit categories that are empty

        ### asana_tasks[]
        - Extract concrete action items that should be tracked as Asana tasks
        - Overlap with focus_updates.next_actions is fine — include here if it deserves tracking
        - Do NOT include tasks already present in the "Existing Asana tasks" section of the prompt
        - priority: "High" for urgent/blocking, "Medium" for normal, "Low" for nice-to-have, "" if unclear
        - notes: one sentence of context if helpful, otherwise omit
        - Omit if no trackable tasks detected

        ## General rules
        - Output ONLY the JSON. No explanation, no markdown fences.
        - If a section has no items, use an empty array [].
        - If focus_updates has no items, use empty arrays.
        """;

    private static string BuildUserPrompt(
        string meetingNotes,
        string projectName,
        string? workstreamId,
        string currentFocus,
        string currentOpenIssues,
        string currentAsanaSummary)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Meeting notes to analyze");
        sb.AppendLine(meetingNotes);
        sb.AppendLine();
        sb.AppendLine("## Context");
        sb.AppendLine($"- Project: {projectName}");
        sb.AppendLine($"- Workstream: {workstreamId ?? "general"}");
        sb.AppendLine($"- Date: {DateTime.Now:yyyy-MM-dd}");
        sb.AppendLine();
        sb.AppendLine("## Existing open issues (to avoid duplicates)");
        sb.AppendLine(string.IsNullOrWhiteSpace(currentOpenIssues) ? "(none)" : currentOpenIssues);
        sb.AppendLine();
        sb.AppendLine("## Existing focus (for context)");
        sb.AppendLine(string.IsNullOrWhiteSpace(currentFocus) ? "(none)" : currentFocus);
        sb.AppendLine();
        sb.AppendLine("## Existing Asana tasks (to avoid duplicates in asana_tasks output)");
        sb.AppendLine(string.IsNullOrWhiteSpace(currentAsanaSummary) ? "(none)" : currentAsanaSummary);
        return sb.ToString();
    }

    // =========================================================================
    // JSON パース
    // =========================================================================
    private static MeetingAnalysisResult ParseResult(string raw)
    {
        try
        {
            var json = raw.Trim();
            if (json.StartsWith("```"))
                json = Regex.Replace(json, @"```[a-z]*", "").Trim('`').Trim();

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var result = new MeetingAnalysisResult();

            // decisions
            if (root.TryGetProperty("decisions", out var decisionsEl) &&
                decisionsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var d in decisionsEl.EnumerateArray())
                {
                    result.Decisions.Add(new MeetingDecision
                    {
                        FilenameTopic  = GetStr(d, "filename_topic"),
                        Title          = GetStr(d, "title"),
                        Status         = GetStr(d, "status"),
                        Trigger        = GetStr(d, "trigger"),
                        Context        = GetStr(d, "context"),
                        OptionAName    = GetStr(d, "option_a_name"),
                        OptionAPros    = GetStr(d, "option_a_pros"),
                        OptionACons    = GetStr(d, "option_a_cons"),
                        OptionBName    = GetStr(d, "option_b_name"),
                        OptionBPros    = GetStr(d, "option_b_pros"),
                        OptionBCons    = GetStr(d, "option_b_cons"),
                        Chosen         = GetStr(d, "chosen"),
                        Why            = GetStr(d, "why"),
                        Risk           = GetStr(d, "risk"),
                        RevisitTrigger = GetStr(d, "revisit_trigger"),
                        IsSelected     = true,
                    });
                }
            }

            // focus_updates
            if (root.TryGetProperty("focus_updates", out var focusEl) &&
                focusEl.ValueKind == JsonValueKind.Object)
            {
                result.FocusUpdate.RecentContext   = GetStrList(focusEl, "recent_context");
                result.FocusUpdate.NextActions     = GetStrList(focusEl, "next_actions");
                result.FocusUpdate.ProposedContent = GetStr(focusEl, "proposed_content");
            }

            // tensions
            if (root.TryGetProperty("tensions", out var tensionsEl) &&
                tensionsEl.ValueKind == JsonValueKind.Object)
            {
                result.Tensions.OpenQuestions = GetStrList(tensionsEl, "open_questions");
                result.Tensions.Concerns      = GetStrList(tensionsEl, "concerns");
            }

            // asana_tasks
            if (root.TryGetProperty("asana_tasks", out var asanaEl) &&
                asanaEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var t in asanaEl.EnumerateArray())
                {
                    result.AsanaTasks.Tasks.Add(new MeetingAsanaTask
                    {
                        Title    = GetStr(t, "title"),
                        Priority = GetStr(t, "priority"),
                        Notes    = GetStr(t, "notes"),
                        IsSelected = true,
                    });
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MeetingNotesService] ParseResult error: {ex.Message}");
            return new MeetingAnalysisResult();
        }
    }

    private static string GetStr(JsonElement el, string key)
    {
        return el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? ""
            : "";
    }

    private static List<string> GetStrList(JsonElement el, string key)
    {
        if (!el.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return [];
        return arr.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString() ?? "")
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();
    }

    // =========================================================================
    // FocusUpdate: ProposedContent 組み立て
    // =========================================================================
    private static string BuildFocusProposed(string currentContent, MeetingFocusUpdate focusUpdate)
    {
        var lines = currentContent.Split('\n').ToList();

        if (focusUpdate.RecentContext.Count > 0)
            lines = InsertItemsAfterSection(lines, SectionRecentContext, focusUpdate.RecentContext);

        if (focusUpdate.NextActions.Count > 0)
            lines = InsertItemsAfterSection(lines, SectionNextActions, focusUpdate.NextActions);

        return string.Join('\n', lines);
    }

    private static List<string> InsertItemsAfterSection(
        List<string> lines, string sectionHeading, List<string> items)
    {
        // セクション見出し行を探す
        int sectionIdx = -1;
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].TrimEnd().Equals(sectionHeading, StringComparison.OrdinalIgnoreCase))
            {
                sectionIdx = i;
                break;
            }
        }

        if (sectionIdx < 0)
        {
            // セクションがなければ末尾に追加
            lines.Add("");
            lines.Add(sectionHeading);
            foreach (var item in items)
                lines.Add($"- {item}");
            return lines;
        }

        // セクションの末尾 (次のセクション見出しの直前) を探す
        int insertIdx = sectionIdx + 1;
        while (insertIdx < lines.Count && !lines[insertIdx].StartsWith("## "))
            insertIdx++;

        // 空行を除いた末尾を探して直前に挿入
        int lastContentLine = insertIdx - 1;
        while (lastContentLine > sectionIdx && string.IsNullOrWhiteSpace(lines[lastContentLine]))
            lastContentLine--;

        var newItems = items.Select(it => $"- {it}").ToList();
        lines.InsertRange(lastContentLine + 1, newItems);
        return lines;
    }

    // =========================================================================
    // Open Issues: AppendContent 組み立て (プレビュー用テキスト)
    // =========================================================================
    private static string BuildOpenIssuesAppend(MeetingTensions tensions)
    {
        var sb = new StringBuilder();
        if (tensions.OpenQuestions.Count > 0)
        {
            sb.AppendLine("## Open questions");
            foreach (var q in tensions.OpenQuestions)
                sb.AppendLine($"- {q}");
            sb.AppendLine();
        }
        if (tensions.Concerns.Count > 0)
        {
            sb.AppendLine("## Risks and concerns");
            foreach (var c in tensions.Concerns)
                sb.AppendLine($"- {c}");
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    // =========================================================================
    // Open Issues: open_issues.md への実際の書き込み内容を生成
    // =========================================================================
    private static string ApplyOpenIssuesToContent(string existingContent, MeetingTensions tensions)
    {
        var lines = existingContent.Split('\n').ToList();

        var sectionItems = new (string[] Candidates, List<string> Items)[]
        {
            (OpenIssueSections[0].Candidates, tensions.OpenQuestions),
            (OpenIssueSections[1].Candidates, tensions.Concerns),
        };

        foreach (var (candidates, items) in sectionItems)
        {
            if (items.Count == 0) continue;
            lines = InsertOpenIssueItems(lines, candidates, items);
        }

        // Last Update 行を更新 / 追加
        lines = UpdateLastUpdateLine(lines);

        return string.Join('\n', lines);
    }

    private static List<string> InsertOpenIssueItems(
        List<string> lines, string[] sectionCandidates, List<string> items)
    {
        // 既存セクション見出しを探す
        int sectionIdx = -1;
        for (int i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].TrimEnd();
            if (sectionCandidates.Any(c => trimmed.Equals(c, StringComparison.OrdinalIgnoreCase)))
            {
                sectionIdx = i;
                break;
            }
        }

        if (sectionIdx < 0)
        {
            // セクションが存在しない場合は末尾に追加
            if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1]))
                lines.Add("");
            lines.Add(sectionCandidates[0]); // 日本語見出し
            foreach (var item in items)
                lines.Add($"- {item}");
            return lines;
        }

        // セクション末尾 (次の ## の直前) を探してそこに挿入
        int insertIdx = sectionIdx + 1;
        while (insertIdx < lines.Count && !lines[insertIdx].StartsWith("## "))
            insertIdx++;

        int lastContentLine = insertIdx - 1;
        while (lastContentLine > sectionIdx && string.IsNullOrWhiteSpace(lines[lastContentLine]))
            lastContentLine--;

        var newItems = items.Select(it => $"- {it}").ToList();
        lines.InsertRange(lastContentLine + 1, newItems);
        return lines;
    }

    private static List<string> UpdateLastUpdateLine(List<string> lines)
    {
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        var lastUpdatePattern = new Regex(@"^(Last Update|Last Updated|更新)[:：]\s*\d{4}-\d{2}-\d{2}", RegexOptions.IgnoreCase);

        for (int i = 0; i < lines.Count; i++)
        {
            if (lastUpdatePattern.IsMatch(lines[i]))
            {
                lines[i] = Regex.Replace(lines[i], @"\d{4}-\d{2}-\d{2}", today);
                return lines;
            }
        }

        // なければ末尾に追加
        if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1]))
            lines.Add("");
        lines.Add($"Last Update: {today}");
        return lines;
    }

    // =========================================================================
    // AsanaTasks: AppendContent 組み立て (プレビュー用テキスト)
    // =========================================================================
    private static string BuildAsanaAppend(MeetingAsanaTasks asanaTasks)
    {
        var sb = new StringBuilder();
        foreach (var task in asanaTasks.Tasks)
        {
            var priorityTag = task.Priority is "High" or "Medium" or "Low"
                ? $" [{task.Priority}]" : "";
            sb.AppendLine($"- [ ] {task.Title}{priorityTag}");
            if (!string.IsNullOrWhiteSpace(task.Notes))
                sb.AppendLine($"    > {task.Notes}");
        }
        return sb.ToString().TrimEnd();
    }

    private static string FormatAsanaTasksForPrompt(AsanaTaskParseResult parsed)
    {
        var tasks = parsed.InProgress.Concat(parsed.NotStarted).Take(40).ToList();
        if (tasks.Count == 0) return "(none)";
        var sb = new StringBuilder();
        foreach (var t in parsed.InProgress)
            sb.AppendLine($"- [InProgress] {t.Title}");
        foreach (var t in parsed.NotStarted.Take(40 - parsed.InProgress.Count))
            sb.AppendLine($"- [NotStarted] {t.Title}");
        return sb.ToString().TrimEnd();
    }

    // =========================================================================
    // Decision Log コンテンツ生成
    // =========================================================================
    public static string BuildDecisionLogContent(MeetingDecision d)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {d.Title}");
        sb.AppendLine();
        sb.AppendLine($"Date: {DateTime.Now:yyyy-MM-dd}");
        sb.AppendLine($"Status: {d.Status}");
        sb.AppendLine($"Trigger: {d.Trigger}");
        sb.AppendLine();
        sb.AppendLine("## Context");
        sb.AppendLine();
        sb.AppendLine(d.Context);
        sb.AppendLine();
        sb.AppendLine("## Options");
        sb.AppendLine();
        sb.AppendLine($"### Option A: {d.OptionAName}");
        sb.AppendLine();
        sb.AppendLine($"Pros: {d.OptionAPros}");
        sb.AppendLine($"Cons: {d.OptionACons}");
        sb.AppendLine();
        sb.AppendLine($"### Option B: {d.OptionBName}");
        sb.AppendLine();
        sb.AppendLine($"Pros: {d.OptionBPros}");
        sb.AppendLine($"Cons: {d.OptionBCons}");
        sb.AppendLine();
        sb.AppendLine("## Decision");
        sb.AppendLine();
        sb.AppendLine($"Chosen: {d.Chosen}");
        sb.AppendLine();
        sb.AppendLine(d.Why);
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(d.Risk))
        {
            sb.AppendLine("## Risk");
            sb.AppendLine();
            sb.AppendLine(d.Risk);
            sb.AppendLine();
        }
        if (!string.IsNullOrWhiteSpace(d.RevisitTrigger))
        {
            sb.AppendLine("## Revisit Trigger");
            sb.AppendLine();
            sb.AppendLine(d.RevisitTrigger);
            sb.AppendLine();
        }
        return sb.ToString();
    }

    // =========================================================================
    // バックアップ (FocusUpdateService と同パターン)
    // =========================================================================
    private async Task CreateFocusBackupAsync(string focusPath)
    {
        try
        {
            var dir     = Path.GetDirectoryName(focusPath)!;
            var histDir = Path.Combine(dir, "focus_history");
            Directory.CreateDirectory(histDir);
            var backupPath = Path.Combine(histDir, $"{DateTime.Now:yyyy-MM-dd}.md");
            if (File.Exists(backupPath)) return;
            var (content, enc) = await _encoding.ReadFileAsync(focusPath);
            await _encoding.WriteFileAsync(backupPath, content, enc);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MeetingNotesService] Focus backup failed: {ex.Message}");
        }
    }

    // =========================================================================
    // Helpers
    // =========================================================================
    private static string BuildNewOpenIssuesTemplate() =>
        "# Open Issues\n\n" +
        "## Open questions\n\n" +
        "## Risks and concerns\n\n";

    private static string SanitizeTopic(string topic)
    {
        if (string.IsNullOrWhiteSpace(topic)) return "decision";
        var invalid = Path.GetInvalidFileNameChars();
        return new string(topic.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim('_', ' ');
    }

    private static string GetUniqueDecisionLogPath(string logDir, string baseFileName)
    {
        var fullPath = Path.Combine(logDir, baseFileName);
        if (!File.Exists(fullPath)) return fullPath;

        var nameWithoutExt = Path.GetFileNameWithoutExtension(baseFileName);
        var ext            = Path.GetExtension(baseFileName);
        foreach (var suffix in "abcdefghijklmnopqrstuvwxyz")
        {
            var candidate = Path.Combine(logDir, $"{nameWithoutExt}_{suffix}{ext}");
            if (!File.Exists(candidate)) return candidate;
        }
        return Path.Combine(logDir, $"{nameWithoutExt}_{DateTime.Now:HHmmss}{ext}");
    }
}
