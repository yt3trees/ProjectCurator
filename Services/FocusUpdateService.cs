using System.IO;
using System.Text;
using System.Threading;
using ProjectCurator.Models;

namespace ProjectCurator.Services;

/// <summary>
/// Asana タスクを解析して current_focus.md の更新提案を生成する。
/// SKILL.md (update-focus-from-asana) の Step 1-6 に相当するオーケストレーション。
/// </summary>
public class FocusUpdateService
{
    private readonly LlmClientService _llm;
    private readonly AsanaTaskParser  _parser;
    private readonly FileEncodingService _encoding;

    public FocusUpdateService(
        LlmClientService llm,
        AsanaTaskParser parser,
        FileEncodingService encoding)
    {
        _llm      = llm;
        _parser   = parser;
        _encoding = encoding;
    }

    // -----------------------------------------------------------------------
    /// <summary>
    /// 更新提案を生成する。
    /// workstreamId が null または空文字 → general モード
    /// workstreamId が指定されている → workstream モード
    /// </summary>
    public async Task<FocusUpdateResult> GenerateProposalAsync(
        ProjectInfo project,
        string? workstreamId,
        CancellationToken ct = default)
    {
        // Step 1: パス解決
        var (focusPath, asanaPath, workMode, resolvedWsId) =
            ResolvePaths(project, workstreamId);

        // Step 2: ファイル存在チェック
        if (!File.Exists(asanaPath))
            throw new FileNotFoundException(
                $"asana-tasks.md が見つかりませんでした: {asanaPath}\n" +
                "Asana sync を先に実行してください。");

        if (!File.Exists(focusPath))
            throw new FileNotFoundException(
                $"current_focus.md が見つかりませんでした: {focusPath}\n" +
                "先に Context Compression Layer のセットアップを行ってください。");

        // Step 3: バックアップ
        var (backupPath, backupStatus) = await CreateBackupAsync(focusPath);

        // Step 4: Asana タスク解析
        var asanaTasks = _parser.ParseFile(asanaPath);

        // Step 5: current_focus.md 読み込み
        var (currentContent, _) = await _encoding.ReadFileAsync(focusPath);

        // Step 6: LLM に更新提案を生成させる
        var systemPrompt = BuildSystemPrompt();
        var userPrompt   = BuildUserPrompt(project.Name, workstreamId, currentContent, asanaTasks);
        var proposed     = await _llm.ChatCompletionAsync(systemPrompt, userPrompt, ct);

        // サマリ生成
        var summary = BuildSummary(asanaTasks, workMode, resolvedWsId);

        return new FocusUpdateResult
        {
            CurrentContent  = currentContent,
            ProposedContent = proposed.Trim(),
            BackupPath      = backupPath,
            BackupStatus    = backupStatus,
            TargetFocusPath = focusPath,
            WorkMode        = workMode,
            WorkstreamId    = resolvedWsId,
            Summary         = summary
        };
    }

    // -----------------------------------------------------------------------
    private static (string focusPath, string asanaPath, WorkMode workMode, string wsId)
        ResolvePaths(ProjectInfo project, string? workstreamId)
    {
        var aiCtxContent  = project.AiContextContentPath;
        var obsidianNotes = Path.Combine(project.AiContextPath, "obsidian_notes");

        if (!string.IsNullOrWhiteSpace(workstreamId))
        {
            // workstream モード
            var wsFocusPath  = Path.Combine(aiCtxContent, "workstreams", workstreamId, "current_focus.md");
            var wsAsanaPath  = Path.Combine(obsidianNotes, "workstreams", workstreamId, "asana-tasks.md");

            // workstream の current_focus.md がなければ general にフォールバック
            var focusPath = File.Exists(wsFocusPath)
                ? wsFocusPath
                : Path.Combine(aiCtxContent, "current_focus.md");

            // workstream の asana-tasks.md がなければ root にフォールバック
            var asanaPath = File.Exists(wsAsanaPath)
                ? wsAsanaPath
                : Path.Combine(obsidianNotes, "asana-tasks.md");

            return (focusPath, asanaPath, WorkMode.SharedWork, workstreamId);
        }
        else
        {
            // general モード
            var focusPath = Path.Combine(aiCtxContent, "current_focus.md");
            var asanaPath = Path.Combine(obsidianNotes, "asana-tasks.md");
            return (focusPath, asanaPath, WorkMode.General, "");
        }
    }

    private async Task<(string path, BackupStatus status)> CreateBackupAsync(string focusPath)
    {
        var dir     = Path.GetDirectoryName(focusPath)!;
        var histDir = Path.Combine(dir, "focus_history");
        Directory.CreateDirectory(histDir);

        var backupPath = Path.Combine(histDir, $"{DateTime.Now:yyyy-MM-dd}.md");
        if (File.Exists(backupPath))
            return (backupPath, BackupStatus.AlreadyExists);

        var (content, enc) = await _encoding.ReadFileAsync(focusPath);
        await _encoding.WriteFileAsync(backupPath, content, enc);
        return (backupPath, BackupStatus.Created);
    }

    // -----------------------------------------------------------------------
    // プロンプト設計
    // -----------------------------------------------------------------------
    private static string BuildSystemPrompt() => """
        You are an assistant that updates current_focus.md based on Asana task data.

        ## Output rules

        - Output ONLY the full updated content of current_focus.md. No explanations, no preamble, no markdown fences.
        - Never truncate. Always output the complete file from the first line to the last.

        ## Update rules

        1. PRESERVE all lines originally written by the user unless a task is clearly completed.
        2. PRESERVE the existing Markdown heading/section structure exactly (do not add, remove, or rename sections).
        3. For each in-progress Asana task ([担当] owner tasks with 🔄 or high priority [ ]):
           - If a matching item already exists in the file → keep it as is (no duplicate).
           - If it is missing from "What I'm working on" or "Next up" sections → add it.
        4. For each completed Asana task (✅ or [x]):
           - If a matching item exists in the file → append " [完了]" to the end of that line.
           - Do NOT delete the line.
        5. [コラボ] (collaborator) tasks:
           - Exclude them unless already present in the file.
           - If already present → keep them (do not add [完了] based on collab tasks alone).
           - Exception: if a collab task is due today or tomorrow AND clearly requires the user's action, you may mention it, prefixed with [コラボ].
        6. Update the "更新: YYYY-MM-DD" date line at the bottom with today's date.
        7. Do not fabricate information. Only use data present in the provided task lists.
        """;

    private static string BuildUserPrompt(
        string projectName,
        string? workstreamId,
        string currentContent,
        AsanaTaskParseResult asanaTasks)
    {
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        var sb = new StringBuilder();

        sb.AppendLine($"## Context");
        sb.AppendLine($"- Project: {projectName}");
        if (!string.IsNullOrWhiteSpace(workstreamId))
            sb.AppendLine($"- Workstream: {workstreamId}");
        sb.AppendLine($"- Today: {today}");
        sb.AppendLine();

        sb.AppendLine("## Current current_focus.md");
        sb.AppendLine(currentContent);
        sb.AppendLine();

        sb.AppendLine("## Asana tasks: In-progress [担当]");
        if (asanaTasks.InProgress.Count == 0)
        {
            sb.AppendLine("(none)");
        }
        else
        {
            foreach (var t in asanaTasks.InProgress)
            {
                var due = t.DueDate != null ? $"  [due: {t.DueDate}]" : "";
                var prio = !string.IsNullOrEmpty(t.Priority) ? $"  [priority: {t.Priority}]" : "";
                sb.AppendLine($"- {t.Title}{due}{prio}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Asana tasks: Completed [担当]");
        if (asanaTasks.Completed.Count == 0)
        {
            sb.AppendLine("(none)");
        }
        else
        {
            foreach (var t in asanaTasks.Completed)
                sb.AppendLine($"- {t.Title}");
        }

        sb.AppendLine();
        var highPriorityNotStarted = asanaTasks.NotStarted
            .Where(t => t.Priority is "最高" or "High")
            .ToList();
        sb.AppendLine("## Asana tasks: Not started, high priority [担当]");
        if (highPriorityNotStarted.Count == 0)
        {
            sb.AppendLine("(none)");
        }
        else
        {
            foreach (var t in highPriorityNotStarted)
            {
                var due = t.DueDate != null ? $"  [due: {t.DueDate}]" : "";
                sb.AppendLine($"- {t.Title}{due}");
            }
        }

        // 期日が近いコラボタスク (今日・翌日) を参考情報として渡す
        var urgentCollab = asanaTasks.Collaborating
            .Where(t => t.DueDate != null &&
                DateTime.TryParse(t.DueDate, out var d) &&
                (d.Date - DateTime.Today).TotalDays <= 1)
            .ToList();
        if (urgentCollab.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Asana tasks: Urgent collab [コラボ] (due today or tomorrow — include only if user action clearly needed)");
            foreach (var t in urgentCollab)
                sb.AppendLine($"- {t.Title}  [due: {t.DueDate}]");
        }

        sb.AppendLine();
        sb.AppendLine("## Instruction");
        sb.AppendLine("Apply the update rules to produce the complete updated current_focus.md.");
        sb.AppendLine("Output the full file content only. Do not include any explanation.");

        return sb.ToString();
    }

    // -----------------------------------------------------------------------
    /// <summary>
    /// 既存の提案に対してユーザーの自然言語指示を適用し、更新後の全文を返す。
    /// </summary>
    public async Task<string> RefineAsync(
        string originalContent,
        string currentProposed,
        string instructions,
        CancellationToken ct = default)
    {
        var systemPrompt = BuildSystemPrompt();
        var userPrompt = $"""
            ## Original current_focus.md

            ```markdown
            {originalContent}
            ```

            ## Current proposal

            ```markdown
            {currentProposed}
            ```

            ## Refinement instruction

            {instructions}

            Please revise the current proposal according to the refinement instruction above.
            Output only the full revised content of current_focus.md.
            """;

        var refined = await _llm.ChatCompletionAsync(systemPrompt, userPrompt, ct);
        return refined.Trim();
    }

    private static string BuildSummary(
        AsanaTaskParseResult asanaTasks, WorkMode workMode, string wsId)
    {
        var sb = new StringBuilder();
        if (workMode == WorkMode.SharedWork && !string.IsNullOrEmpty(wsId))
            sb.AppendLine($"Workstream: {wsId}");

        sb.AppendLine($"進行中タスク: {asanaTasks.InProgress.Count} 件");
        sb.AppendLine($"完了タスク: {asanaTasks.Completed.Count} 件");
        sb.AppendLine($"未着手タスク: {asanaTasks.NotStarted.Count} 件");

        return sb.ToString().Trim();
    }
}
