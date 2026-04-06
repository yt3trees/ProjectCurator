using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Curia.Models;

namespace Curia.Services;

/// <summary>
/// Wiki に対する質問を LLM で回答するパイプライン。
/// </summary>
public class WikiQueryService
{
    private readonly LlmClientService _llm;
    private readonly WikiService _wiki;
    private readonly ConfigService _configService;
    private const int MaxRelevantPages = 5;

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions SaveOpts = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    private string? _currentSessionFilePath;
    private List<(string role, string content)> _conversationHistory = [];
    private List<string> _lastSelectedPaths = [];
    private const int MaxTurns = 10; // 上限ターン数 (user+assistant ペア)

    public WikiQueryService(LlmClientService llm, WikiService wiki, ConfigService configService)
    {
        _llm = llm;
        _wiki = wiki;
        _configService = configService;
    }

    // ---- セッション管理 ----

    public void StartNewSession(string wikiRoot, string sessionId)
    {
        var dir = WikiService.GetQueryHistoryDir(wikiRoot);
        Directory.CreateDirectory(dir);
        _currentSessionFilePath = Path.Combine(dir, $"{sessionId}.json");
        ResetConversation();
    }

    /// <summary>会話履歴をリセットする (Wikiコンテキストは維持)。</summary>
    public void ResetConversation()
    {
        _conversationHistory.Clear();
        _lastSelectedPaths.Clear();
    }

    private async Task AppendToCurrentSessionAsync(WikiQueryRecord record, CancellationToken ct)
    {
        if (_currentSessionFilePath == null) return;
        List<WikiQueryRecord> records = [];
        if (File.Exists(_currentSessionFilePath))
        {
            try
            {
                var existing = await File.ReadAllTextAsync(_currentSessionFilePath, Encoding.UTF8, ct);
                records = JsonSerializer.Deserialize<List<WikiQueryRecord>>(existing, SaveOpts) ?? [];
            }
            catch { records = []; }
        }
        records.Add(record);
        await File.WriteAllTextAsync(
            _currentSessionFilePath,
            JsonSerializer.Serialize(records, SaveOpts),
            Encoding.UTF8, ct);
    }

    public static async Task<List<WikiQueryRecord>> LoadSessionFileAsync(
        string filePath,
        CancellationToken ct = default)
    {
        if (!File.Exists(filePath)) return [];
        try
        {
            var json = await File.ReadAllTextAsync(filePath, Encoding.UTF8, ct);
            return JsonSerializer.Deserialize<List<WikiQueryRecord>>(json, SaveOpts) ?? [];
        }
        catch { return []; }
    }

    /// <summary>指定レコードをセッションファイルから削除する。</summary>
    public async Task DeleteRecordAsync(string wikiRoot, WikiQueryRecord record, CancellationToken ct = default)
    {
        var dir = WikiService.GetQueryHistoryDir(wikiRoot);
        if (!Directory.Exists(dir)) return;
        foreach (var file in Directory.GetFiles(dir, "*.json"))
        {
            try
            {
                var json = await File.ReadAllTextAsync(file, Encoding.UTF8, ct);
                var records = JsonSerializer.Deserialize<List<WikiQueryRecord>>(json, SaveOpts) ?? [];
                var before = records.Count;
                records.RemoveAll(r => r.AskedAt == record.AskedAt && r.Question == record.Question);
                if (records.Count == before) continue;
                if (records.Count == 0)
                    File.Delete(file);
                else
                    await File.WriteAllTextAsync(file, JsonSerializer.Serialize(records, SaveOpts), Encoding.UTF8, ct);
                return;
            }
            catch { }
        }
    }

    /// <summary>現セッションを除いた過去セッションファイルを新しい順で返す。</summary>
    public static List<string> GetPastSessionFiles(string wikiRoot, string currentSessionId)
    {
        var dir = WikiService.GetQueryHistoryDir(wikiRoot);
        if (!Directory.Exists(dir)) return [];
        return [.. Directory.GetFiles(dir, "*.json")
            .Where(f => !Path.GetFileNameWithoutExtension(f)
                .Equals(currentSessionId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f)];
    }

    // ---- クエリ ----

    public async Task<WikiQueryRecord> Query(
        string wikiRoot,
        string question,
        CancellationToken cancellationToken = default)
    {
        var index = _wiki.GetIndex(wikiRoot);
        var schemaPath = WikiService.GetSchemaPath(wikiRoot);
        var schema = File.Exists(schemaPath) ? await File.ReadAllTextAsync(schemaPath, Encoding.UTF8, cancellationToken) : "";

        // Step 1: 関連ページ選択。前回と同じページが選ばれた場合はコンテンツ埋め込みをスキップ
        var pages = _wiki.GetAllPages(wikiRoot);
        var (relevantContent, selectedPaths) = await GetRelevantContent(wikiRoot, index, question, pages, cancellationToken);

        bool sameAsLastTurn = _conversationHistory.Count >= 2
            && selectedPaths.Count > 0
            && selectedPaths.OrderBy(x => x).SequenceEqual(_lastSelectedPaths.OrderBy(x => x));
        if (sameAsLastTurn)
            relevantContent = string.Empty; // 履歴にすでに存在するので再送不要

        _lastSelectedPaths = selectedPaths;

        // Step 2: 回答生成 (会話履歴付き)
        var systemPrompt = BuildAnswerSystemPrompt(schema);
        var userPrompt = BuildAnswerUserPrompt(question, index, relevantContent);
        _conversationHistory.Add(("user", userPrompt));

        // スライディングウィンドウ: MaxTurns ペアを超えたら古い user+assistant を削除
        while (_conversationHistory.Count > MaxTurns * 2)
            _conversationHistory.RemoveRange(0, 2);

        var rawAnswer = await _llm.ChatWithHistoryAsync(systemPrompt, _conversationHistory, cancellationToken);
        _conversationHistory.Add(("assistant", rawAnswer));

        // Step 3: 参照ページ = コンテキストページ + LLM回答から [[...]] 抽出の合算
        var contextTitles = selectedPaths
            .Select(path => pages.FirstOrDefault(p =>
                string.Equals(p.RelativePath, path, StringComparison.OrdinalIgnoreCase))?.Title
                ?? Path.GetFileNameWithoutExtension(path))
            .Where(t => !string.IsNullOrEmpty(t));
        var citedTitles = ExtractReferencedPages(rawAnswer, pages);
        var referencedPages = contextTitles.Concat(citedTitles).Distinct().ToList();

        // Step 4: log に記録
        var logEntry = $"\n## [{DateTime.Now:yyyy-MM-dd}] query | {question}\n- 参照ページ: {string.Join(", ", referencedPages)}\n";
        await _wiki.AppendLog(wikiRoot, logEntry);

        await _wiki.UpdateMeta(wikiRoot, m => m.Stats.LastQuery = DateTime.UtcNow);

        var record = new WikiQueryRecord
        {
            AskedAt = DateTime.Now,
            Question = question,
            Answer = rawAnswer,
            ReferencedPages = referencedPages
        };

        // Step 5: セッションファイルに保存
        await AppendToCurrentSessionAsync(record, cancellationToken);

        return record;
    }

    public async Task<string> SaveAnswerAsPage(
        string wikiRoot,
        WikiQueryRecord record,
        CancellationToken cancellationToken = default)
    {
        var date = record.AskedAt.ToString("yyyy-MM-dd");
        var slug = Slugify(record.Question);
        var relativePath = $"pages/analysis/{date}-{slug}.md";

        var content = $"""
---
title: "{record.Question}"
created: "{date}"
updated: "{date}"
tags: [analysis, qa]
---

## Question

{record.Question}

## Answer

{record.Answer}

## Referenced Pages

{string.Join("\n", record.ReferencedPages.Select(p => $"- [[{p}]]"))}
""";

        await _wiki.SavePage(wikiRoot, relativePath, content);

        var logEntry = $"\n## [{date}] query-saved | {record.Question}\n- 保存先: {relativePath}\n";
        await _wiki.AppendLog(wikiRoot, logEntry);

        record.SavedAsPage = relativePath;
        return relativePath;
    }

    // ---- 関連ページ取得 ----

    private async Task<(string content, List<string> selectedPaths)> GetRelevantContent(
        string wikiRoot,
        string index,
        string question,
        IReadOnlyList<WikiPageItem> pages,
        CancellationToken cancellationToken)
    {
        var nonRootPages = pages.Where(x => !x.IsRoot).ToList();
        if (nonRootPages.Count == 0)
            return ("", []);

        var selectedPaths = await SelectRelevantPagePaths(index, question, nonRootPages, cancellationToken);
        if (selectedPaths.Count == 0)
        {
            // フォールバック: タイトル/パスのキーワード一致で補完
            selectedPaths = FallbackSelectByKeywords(question, nonRootPages)
                .Take(MaxRelevantPages)
                .ToList();
        }

        var sb = new StringBuilder();
        foreach (var path in selectedPaths.Take(MaxRelevantPages))
        {
            var item = _wiki.GetPage(wikiRoot, path);
            if (item == null) continue;
            sb.AppendLine($"### [{path}]");
            sb.AppendLine(item.Content);
            sb.AppendLine();
        }
        return (sb.ToString(), selectedPaths.Take(MaxRelevantPages).ToList());
    }

    private async Task<List<string>> SelectRelevantPagePaths(
        string index,
        string question,
        IReadOnlyList<WikiPageItem> pages,
        CancellationToken cancellationToken)
    {
        // 実際のパス一覧を明示的に渡し、そこから選ばせる
        var pageList = string.Join("\n", pages.Select(p => $"{p.RelativePath}\t{p.Title}"));
        var selectionPrompt = $"""
Question: "{question}"

Pick the {MaxRelevantPages} most relevant pages. Copy the paths EXACTLY as listed below (left column):
{pageList}

Wiki index (for context only):
{index}
""";
        var selected = await _llm.ChatCompletionAsync(
            "You are a wiki search assistant. Output file paths only, one per line. Copy each path character-for-character from the provided list.",
            selectionPrompt,
            cancellationToken);

        var existing = new HashSet<string>(pages.Select(p => p.RelativePath), StringComparer.OrdinalIgnoreCase);
        return selected
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(path => path.Trim('`', ' ', '-', '*').Split('\t')[0].Trim())
            .Where(path => existing.Contains(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxRelevantPages)
            .ToList();
    }

    private static IEnumerable<string> FallbackSelectByKeywords(string question, IReadOnlyList<WikiPageItem> pages)
    {
        var tokens = question
            .Split([' ', '\t', '\r', '\n', '　', ',', '.', '、', '。', ':', '：', ';', '；', '/', '\\', '(', ')', '[', ']', '{', '}', '"', '\''],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length >= 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (tokens.Length == 0)
            return pages.Select(p => p.RelativePath);

        var scored = pages
            .Select(p => new
            {
                p.RelativePath,
                p.LastModified,
                Score = tokens.Count(t =>
                    p.Title.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                    p.RelativePath.Contains(t, StringComparison.OrdinalIgnoreCase))
            })
            .ToList();

        var matched = scored.Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.RelativePath);

        // キーワード一致なし → 最近更新されたページを返す
        if (!matched.Any())
            return scored.OrderByDescending(x => x.LastModified).Select(x => x.RelativePath);

        return matched;
    }

    // ---- プロンプト構築 ----

    private string BuildAnswerSystemPrompt(string schema)
    {
        var language = _configService.LoadSettings().LlmLanguage;
        return $"""
You are a knowledgeable assistant for a project wiki.
Answer questions based ONLY on the provided wiki content.
If the answer is not in the wiki, say so clearly.
Always cite the pages you referenced using [[PageName]] format.
Write your answer in {language}.

Project context:
{schema}
""";
    }

    private static string BuildAnswerUserPrompt(string question, string index, string pageContents) => $"""
## Question
{question}

## Wiki Index
{index}

## Relevant Page Contents
{pageContents}

Please provide a clear, concise answer based on the wiki content above.
End your response with: "Referenced pages: [[page1]], [[page2]], ..."
""";

    // ---- ユーティリティ ----

    private static List<string> ExtractReferencedPages(string answer, IReadOnlyList<WikiPageItem> pages)
    {
        var referenced = new List<string>();
        var links = WikiLinkParser.ExtractLinks(answer);
        foreach (var link in links)
        {
            var match = pages.FirstOrDefault(p =>
                string.Equals(p.Title, link, StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileNameWithoutExtension(p.RelativePath).Equals(link, StringComparison.OrdinalIgnoreCase));
            if (match != null)
                referenced.Add(match.Title);
        }
        return referenced.Distinct().ToList();
    }

    private static string Slugify(string text)
    {
        var slug = text.Length > 40 ? text[..40] : text;
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"[^\w\s-]", "");
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"\s+", "-");
        return slug.Trim('-').ToLowerInvariant();
    }
}
