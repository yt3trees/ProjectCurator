using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Curia.Models;
using System.Collections.Generic;

namespace Curia.Services;

/// <summary>
/// Wiki の整合性チェック (静的 + LLM) を行う。
/// </summary>
public class WikiLintService
{
    private readonly LlmClientService _llm;
    private readonly WikiService _wiki;
    private readonly ConfigService _configService;

    public WikiLintService(LlmClientService llm, WikiService wiki, ConfigService configService)
    {
        _llm = llm;
        _wiki = wiki;
        _configService = configService;
    }

    public async Task<WikiLintResult> RunLint(
        string wikiRoot,
        bool useLlm = true,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var issues = new List<WikiLintIssue>();
        var pages = _wiki.GetAllPages(wikiRoot);

        // --- 静的チェック (C# のみ) ---
        progress?.Report("Checking broken links...");
        issues.AddRange(CheckBrokenLinks(wikiRoot, pages));

        progress?.Report("Checking orphan pages...");
        issues.AddRange(CheckOrphanPages(wikiRoot, pages));

        progress?.Report("Checking missing sources...");
        issues.AddRange(CheckMissingSources(wikiRoot, pages));

        progress?.Report("Checking stale pages (by age)...");
        issues.AddRange(CheckStalePage(pages));

        // --- LLM チェック ---
        if (useLlm)
        {
            progress?.Report("Running LLM analysis (contradiction, missing pages)...");
            try
            {
                var llmIssues = await RunLlmLint(wikiRoot, pages, cancellationToken);
                issues.AddRange(llmIssues);
            }
            catch (Exception ex)
            {
                issues.Add(new WikiLintIssue
                {
                    Category = "LLM Error",
                    Severity = WikiLintSeverity.Low,
                    Description = $"LLM lint could not run: {ex.Message}"
                });
            }
        }

        var result = new WikiLintResult
        {
            RunAt = DateTime.Now,
            Issues = issues
        };

        // log に記録
        var summary = $"Lint: {issues.Count} issues found ({issues.Count(i => i.Severity == WikiLintSeverity.High)} high, {issues.Count(i => i.Severity == WikiLintSeverity.Medium)} medium, {issues.Count(i => i.Severity == WikiLintSeverity.Low)} low)";
        await _wiki.AppendLog(wikiRoot, $"\n## [{DateTime.Now:yyyy-MM-dd}] lint | {summary}\n");
        await _wiki.UpdateMeta(wikiRoot, m => m.Stats.LastLint = DateTime.UtcNow);

        return result;
    }

    // ---- 静的チェック ----

    private static IEnumerable<WikiLintIssue> CheckBrokenLinks(string wikiRoot, IReadOnlyList<WikiPageItem> pages)
    {
        foreach (var page in pages)
        {
            var item = LoadContent(wikiRoot, page);
            if (item == null) continue;

            var broken = WikiLinkParser.FindBrokenLinks(wikiRoot, item);
            foreach (var link in broken)
            {
                yield return new WikiLintIssue
                {
                    Category = "BrokenLink",
                    Severity = WikiLintSeverity.High,
                    Description = $"Broken link [[{link}]] in {page.RelativePath}",
                    PagePath = page.RelativePath
                };
            }
        }
    }

    private static IEnumerable<WikiLintIssue> CheckOrphanPages(string wikiRoot, IReadOnlyList<WikiPageItem> pages)
    {
        // ルートページ・sources は除外
        var candidates = pages.Where(p => !p.IsRoot && p.Category != "sources").ToList();

        // 全ページのリンクを収集
        var allLinks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var page in pages)
        {
            var content = LoadContent(wikiRoot, page);
            if (content == null) continue;
            foreach (var link in WikiLinkParser.ExtractLinks(content))
                allLinks.Add(link);
        }

        foreach (var page in candidates)
        {
            var stem = Path.GetFileNameWithoutExtension(page.RelativePath);
            if (!allLinks.Contains(page.Title) && !allLinks.Contains(stem))
            {
                yield return new WikiLintIssue
                {
                    Category = "Orphan",
                    Severity = WikiLintSeverity.Low,
                    Description = $"No inbound links to {page.RelativePath}",
                    PagePath = page.RelativePath
                };
            }
        }
    }

    private static IEnumerable<WikiLintIssue> CheckMissingSources(string wikiRoot, IReadOnlyList<WikiPageItem> pages)
    {
        // sources/ ページのフロントマターにある sources: を確認
        var rawDir = WikiService.GetRawDir(wikiRoot);
        foreach (var page in pages.Where(p => p.Category == "sources"))
        {
            var content = LoadContent(wikiRoot, page);
            if (content == null) continue;

            // sources: [...] フロントマターから取得
            var sourcesLine = content.Split('\n')
                .FirstOrDefault(l => l.TrimStart().StartsWith("sources:", StringComparison.OrdinalIgnoreCase));
            if (sourcesLine == null) continue;

            // シンプルな抽出: sources: ["file1", "file2"]
            var matches = System.Text.RegularExpressions.Regex.Matches(sourcesLine, "\"([^\"]+)\"");
            foreach (System.Text.RegularExpressions.Match m in matches)
            {
                var srcFile = m.Groups[1].Value;
                if (!File.Exists(Path.Combine(rawDir, srcFile)))
                {
                    yield return new WikiLintIssue
                    {
                        Category = "MissingSource",
                        Severity = WikiLintSeverity.High,
                        Description = $"Source file '{srcFile}' referenced in {page.RelativePath} not found in raw/",
                        PagePath = page.RelativePath
                    };
                }
            }
        }
    }

    private static IEnumerable<WikiLintIssue> CheckStalePage(IReadOnlyList<WikiPageItem> pages)
    {
        var threshold = DateTime.Now.AddDays(-30);
        foreach (var page in pages.Where(p => !p.IsRoot && p.Category != "sources"))
        {
            if (page.LastModified < threshold)
            {
                var daysAgo = (int)(DateTime.Now - page.LastModified).TotalDays;
                yield return new WikiLintIssue
                {
                    Category = "Stale",
                    Severity = WikiLintSeverity.Medium,
                    Description = $"{page.RelativePath} has not been updated in {daysAgo} days",
                    PagePath = page.RelativePath
                };
            }
        }
    }

    // ---- LLM チェック ----

    private async Task<IEnumerable<WikiLintIssue>> RunLlmLint(
        string wikiRoot,
        IReadOnlyList<WikiPageItem> pages,
        CancellationToken cancellationToken)
    {
        var index = _wiki.GetIndex(wikiRoot);

        // 全ページの1行要約を構築（LLM に渡すトークン削減）
        var sb = new StringBuilder();
        foreach (var page in pages.Where(p => !p.IsRoot).Take(80))
        {
            var content = LoadContent(wikiRoot, page);
            var firstLine = content?.Split('\n').FirstOrDefault(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("---") && !l.StartsWith('#')) ?? "";
            sb.AppendLine($"- {page.RelativePath}: {page.Title} — {firstLine[..Math.Min(100, firstLine.Length)]}");
        }

        const string baseSystemPrompt = """
You are a wiki quality auditor. Analyze the wiki pages for:
1. Contradictions: pages with conflicting descriptions of the same fact
2. Missing pages: topics mentioned in 3+ pages but with no dedicated page

Respond in this exact format (plain text, no JSON):
CONTRADICTION: [page1] vs [page2] — [description]
MISSING: [topic] — mentioned in [page1], [page2], [page3]

If no issues found for a category, write "CONTRADICTION: none" or "MISSING: none"
""";

        var promptConfig = _wiki.LoadPrompts(wikiRoot);
        var overrides = promptConfig.IsUnknownVersion ? null : promptConfig.Lint;

        var systemPromptParts = new List<string>();
        if (overrides != null && !string.IsNullOrEmpty(overrides.SystemPrefix)) systemPromptParts.Add(overrides.SystemPrefix);
        systemPromptParts.Add(baseSystemPrompt);
        if (overrides != null && !string.IsNullOrEmpty(overrides.SystemSuffix)) systemPromptParts.Add(overrides.SystemSuffix);
        var systemPrompt = string.Join("\n\n", systemPromptParts);

        var baseUserPrompt = $"""
Wiki index:
{index}

Page summaries:
{sb}

Identify contradictions and missing pages.
""";
        var userPrompt = (overrides != null && !string.IsNullOrEmpty(overrides.UserSuffix))
            ? baseUserPrompt + "\n\n" + overrides.UserSuffix
            : baseUserPrompt;

        var raw = await _llm.ChatCompletionAsync(systemPrompt, userPrompt, cancellationToken);
        return ParseLlmLintResponse(raw);
    }

    private static IEnumerable<WikiLintIssue> ParseLlmLintResponse(string raw)
    {
        foreach (var line in raw.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("CONTRADICTION:", StringComparison.OrdinalIgnoreCase))
            {
                var desc = line["CONTRADICTION:".Length..].Trim();
                if (desc == "none") continue;
                yield return new WikiLintIssue
                {
                    Category = "Contradiction",
                    Severity = WikiLintSeverity.High,
                    Description = desc
                };
            }
            else if (line.StartsWith("MISSING:", StringComparison.OrdinalIgnoreCase))
            {
                var desc = line["MISSING:".Length..].Trim();
                if (desc == "none") continue;
                yield return new WikiLintIssue
                {
                    Category = "Missing",
                    Severity = WikiLintSeverity.Medium,
                    Description = desc
                };
            }
        }
    }

    /// <summary>UI 表示用デフォルトシステムプロンプトのプレビュー。</summary>
    public static string GetDefaultSystemPromptPreview(string language) => """
You are a wiki quality auditor. Analyze the wiki pages for:
1. Contradictions: pages with conflicting descriptions of the same fact
2. Missing pages: topics mentioned in 3+ pages but with no dedicated page

Respond in this exact format (plain text, no JSON):
CONTRADICTION: [page1] vs [page2] — [description]
MISSING: [topic] — mentioned in [page1], [page2], [page3]

If no issues found for a category, write "CONTRADICTION: none" or "MISSING: none"
""";

    // ---- ヘルパー ----

    private static string? LoadContent(string wikiRoot, WikiPageItem page)
    {
        try
        {
            var path = Path.Combine(wikiRoot, page.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;
        }
        catch { return null; }
    }
}
