using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ProjectCurator.Models;

namespace ProjectCurator.Services;

/// <summary>
/// Wiki に対する質問を LLM で回答するパイプライン。
/// </summary>
public class WikiQueryService
{
    private readonly LlmClientService _llm;
    private readonly WikiService _wiki;

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public WikiQueryService(LlmClientService llm, WikiService wiki)
    {
        _llm = llm;
        _wiki = wiki;
    }

    public async Task<WikiQueryRecord> Query(
        string wikiRoot,
        string question,
        CancellationToken cancellationToken = default)
    {
        var index = _wiki.GetIndex(wikiRoot);
        var schemaPath = WikiService.GetSchemaPath(wikiRoot);
        var schema = File.Exists(schemaPath) ? await File.ReadAllTextAsync(schemaPath, Encoding.UTF8, cancellationToken) : "";

        // Step 1: index.md から関連ページを LLM に選ばせる
        var pages = _wiki.GetAllPages(wikiRoot);
        var relevantContent = await GetRelevantContent(wikiRoot, index, question, pages, cancellationToken);

        // Step 2: 回答生成
        var systemPrompt = BuildAnswerSystemPrompt(schema);
        var userPrompt   = BuildAnswerUserPrompt(question, index, relevantContent);

        var rawAnswer = await _llm.ChatCompletionAsync(systemPrompt, userPrompt, cancellationToken);

        // Step 3: 参照ページを抽出
        var referencedPages = ExtractReferencedPages(rawAnswer, pages);

        // Step 4: log に記録
        var logEntry = $"\n## [{DateTime.Now:yyyy-MM-dd}] query | {question}\n- 参照ページ: {string.Join(", ", referencedPages)}\n";
        await _wiki.AppendLog(wikiRoot, logEntry);

        await _wiki.UpdateMeta(wikiRoot, m => m.Stats.LastQuery = DateTime.UtcNow);

        return new WikiQueryRecord
        {
            AskedAt = DateTime.Now,
            Question = question,
            Answer = rawAnswer,
            ReferencedPages = referencedPages
        };
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

    private async Task<string> GetRelevantContent(
        string wikiRoot,
        string index,
        string question,
        IReadOnlyList<WikiPageItem> pages,
        CancellationToken cancellationToken)
    {
        if (pages.Count <= 100)
        {
            // Small mode: 全ページ内容を直接渡す
            var sb = new StringBuilder();
            foreach (var p in pages.Where(x => !x.IsRoot).Take(50))
            {
                var item = _wiki.GetPage(wikiRoot, p.RelativePath);
                if (item == null) continue;
                sb.AppendLine($"### [{p.RelativePath}]");
                sb.AppendLine(item.Content);
                sb.AppendLine();
            }
            return sb.ToString();
        }

        // Medium+ mode: LLM に index から関連ページを選ばせる
        var selectionPrompt = $"""
Given this question: "{question}"

From the wiki index below, list the 5 most relevant page paths (one per line, path only):
{index}
""";
        var selected = await _llm.ChatCompletionAsync(
            "You are a wiki search assistant. Respond with file paths only, one per line.",
            selectionPrompt,
            cancellationToken);

        var selectedPaths = selected.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var sb2 = new StringBuilder();
        foreach (var path in selectedPaths.Take(5))
        {
            var normalizedPath = path.Trim('`', ' ', '-');
            var item = _wiki.GetPage(wikiRoot, normalizedPath);
            if (item == null) continue;
            sb2.AppendLine($"### [{normalizedPath}]");
            sb2.AppendLine(item.Content);
            sb2.AppendLine();
        }
        return sb2.ToString();
    }

    // ---- プロンプト構築 ----

    private static string BuildAnswerSystemPrompt(string schema)
    {
        var isJapanese = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ja";
        return $"""
You are a knowledgeable assistant for a project wiki.
Answer questions based ONLY on the provided wiki content.
If the answer is not in the wiki, say so clearly.
Always cite the pages you referenced using [[PageName]] format.
Write your answer in {(isJapanese ? "Japanese" : "English")}.

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
