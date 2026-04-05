using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ProjectCurator.Models;

namespace ProjectCurator.Services;

/// <summary>
/// ソースファイルを Wiki に取り込む LLM パイプライン。
/// </summary>
public class WikiIngestService
{
    private readonly LlmClientService _llm;
    private readonly WikiService _wiki;
    private const int MaxUpdateCandidates = 8;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public WikiIngestService(LlmClientService llm, WikiService wiki)
    {
        _llm = llm;
        _wiki = wiki;
    }

    public async Task<IngestResult> IngestSource(
        string wikiRoot,
        string sourceFilePath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var proposal = await GenerateIngestProposal(wikiRoot, sourceFilePath, progress, cancellationToken);
        if (!proposal.Success)
            return proposal;

        await ApplyIngestResult(wikiRoot, proposal, progress, cancellationToken);
        return proposal;
    }

    public async Task<IngestResult> GenerateIngestProposal(
        string wikiRoot,
        string sourceFilePath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // 1. raw/ にコピー
            progress?.Report("Copying source to raw/...");
            _ = await _wiki.AddSource(wikiRoot, sourceFilePath);

            // 2. ソース内容を読み取り
            progress?.Report("Reading source content...");
            var sourceContent = await ReadSourceContent(sourceFilePath);
            if (string.IsNullOrWhiteSpace(sourceContent))
                return Fail("Source file is empty or could not be read.");

            // 3. プロンプト構築
            progress?.Report("Building LLM prompt...");
            var schemaPath = WikiService.GetSchemaPath(wikiRoot);
            var schema = File.Exists(schemaPath) ? await File.ReadAllTextAsync(schemaPath, Encoding.UTF8, cancellationToken) : "";
            var index = _wiki.GetIndex(wikiRoot);
            var pages = _wiki.GetAllPages(wikiRoot);

            var systemPrompt = BuildSystemPrompt(schema);
            progress?.Report("Selecting update candidates...");
            var updateCandidates = await SelectUpdateCandidates(
                wikiRoot,
                index,
                sourceContent,
                pages,
                cancellationToken);

            progress?.Report("Loading candidate pages...");
            var existingPagesContent = BuildExistingPagesContent(wikiRoot, updateCandidates);
            var existingTags = CollectExistingTags(wikiRoot, pages);

            var userPrompt = BuildUserPrompt(
                index,
                sourceContent,
                Path.GetFileName(sourceFilePath),
                existingPagesContent,
                existingTags);

            // 4. LLM 呼び出し
            progress?.Report("Calling LLM...");
            var raw = await _llm.ChatCompletionAsync(systemPrompt, userPrompt, cancellationToken);

            // 5. JSON パース
            progress?.Report("Parsing LLM response...");
            var llmResp = ParseLlmResponse(raw);
            if (llmResp == null)
                return Fail("Failed to parse LLM response as JSON.");
            CanonicalizeResultTags(llmResp, existingTags);

            progress?.Report("Proposal generated. Awaiting review.");

            return new IngestResult
            {
                Success = true,
                Summary = llmResp.Summary,
                NewPages = llmResp.NewPages,
                UpdatedPages = llmResp.UpdatedPages,
                IndexUpdate = llmResp.IndexUpdate,
                LogEntry = llmResp.LogEntry,
                DebugSystemPrompt = _llm.LastSystemPrompt,
                DebugUserPrompt = _llm.LastUserPrompt,
                DebugResponse = _llm.LastResponse
            };
        }
        catch (OperationCanceledException)
        {
            return Fail("Ingest cancelled.");
        }
        catch (Exception ex)
        {
            return Fail($"Ingest error: {ex.Message}");
        }
    }

    public async Task ApplyIngestResult(
        string wikiRoot,
        IngestResult result,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        progress?.Report("Writing approved pages...");
        foreach (var p in result.NewPages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(p.Path) && !string.IsNullOrWhiteSpace(p.Content))
                await _wiki.SavePage(wikiRoot, p.Path, p.Content);
        }

        foreach (var u in result.UpdatedPages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(u.Path) && !string.IsNullOrWhiteSpace(u.Diff))
                await _wiki.SavePage(wikiRoot, u.Path, u.Diff);
        }

        if (!string.IsNullOrWhiteSpace(result.IndexUpdate))
            await _wiki.UpdateIndex(wikiRoot, result.IndexUpdate);

        if (!string.IsNullOrWhiteSpace(result.LogEntry))
            await _wiki.AppendLog(wikiRoot, result.LogEntry);

        await _wiki.UpdateMeta(wikiRoot, m =>
        {
            m.Stats.LastIngest = DateTime.UtcNow;
            m.Stats.TotalSources = _wiki.GetAllSources(wikiRoot).Count;
            m.Stats.TotalPages = _wiki.GetAllPages(wikiRoot).Count(p => !p.IsRoot);
        });

        progress?.Report("Saved.");
    }

    // ---- プロンプト構築 ----

    private static string BuildSystemPrompt(string schema)
    {
        var isJapanese = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ja";
        return $$"""
You are the Wiki maintainer for ProjectCurator.
Follow the wiki-schema.md below exactly.

{{schema}}

IMPORTANT:
- Always respond with valid JSON matching the schema below.
- For updatedPages, return the FULL updated content in the "diff" field (not a patch).
- Keep pages concise and well-structured in Markdown.
- Use [[PageName]] wikilink format for cross-references.
- Write all page content in {{(isJapanese ? "Japanese" : "English")}}.
- Include YAML frontmatter at the top of each page:
  ---
  title: "..."
  created: "YYYY-MM-DD"
  updated: "YYYY-MM-DD"
  sources: ["filename"]
  tags: [tag1, tag2]
  ---

Response JSON schema:
{
  "summary": "string — brief description of what was done",
  "newPages": [{ "path": "pages/category/filename.md", "content": "full markdown" }],
  "updatedPages": [{ "path": "pages/category/filename.md", "diff": "full updated markdown" }],
  "indexUpdate": "full updated index.md content",
  "logEntry": "markdown log entry to append"
}
""";
    }

    private static string BuildUserPrompt(
        string index,
        string sourceContent,
        string fileName,
        string existingPagesContent,
        IReadOnlyList<string> existingTags) => $"""
Please ingest the following source into the Wiki.

## Current index.md
{index}

## Source File: {fileName}
{sourceContent}

## Existing pages available for update (full content)
{existingPagesContent}

## Existing tag vocabulary (reuse these when possible)
{BuildTagVocabulary(existingTags)}

## Instructions
1. Create a summary page in sources/ for this source
2. If updating an existing page, use the "Existing pages available for update" section as the source of truth for current content before editing
3. Create new entity/concept pages if needed
4. Generate the full updated index.md
5. Generate a log.md entry for this ingest
6. Prefer reusing existing tags and avoid near-duplicate variants (case/plural/separator differences)

Respond with JSON only (no markdown code fences).
""";

    private async Task<IReadOnlyList<string>> SelectUpdateCandidates(
        string wikiRoot,
        string index,
        string sourceContent,
        IReadOnlyList<WikiPageItem> pages,
        CancellationToken cancellationToken)
    {
        var existingPages = pages.Where(p => !p.IsRoot).ToList();
        if (existingPages.Count == 0)
            return [];

        var pageList = string.Join('\n', existingPages.Select(p => p.RelativePath));
        var systemPrompt = """
You are a wiki update planner.
From the given wiki index and source document, select existing page paths that are likely to require updates.
Respond with JSON only:
{
  "updateCandidates": ["pages/...md"]
}
Rules:
- Return existing page paths only.
- Return at most 8 paths.
- Do not include index.md or log.md.
""";

        var userPrompt = $"""
## Existing wiki page paths
{pageList}

## Current index.md
{index}

## New source content
{sourceContent}

Select up to {MaxUpdateCandidates} existing pages that most likely need updates.
""";

        var raw = await _llm.ChatCompletionAsync(systemPrompt, userPrompt, cancellationToken);
        var parsed = ParseUpdateSelectionResponse(raw);
        if (parsed.Count == 0)
            return [];

        var existingSet = new HashSet<string>(existingPages.Select(p => p.RelativePath), StringComparer.OrdinalIgnoreCase);
        return parsed
            .Select(NormalizeRelativePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(path => existingSet.Contains(path!))
            .Take(MaxUpdateCandidates)
            .Cast<string>()
            .ToList();
    }

    private static string BuildExistingPagesContent(string wikiRoot, IReadOnlyList<string> candidatePaths)
    {
        if (candidatePaths.Count == 0)
            return "(none)";

        var sb = new StringBuilder();
        foreach (var path in candidatePaths)
        {
            var fullPath = Path.Combine(wikiRoot, path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath)) continue;
            var content = File.ReadAllText(fullPath, Encoding.UTF8);
            sb.AppendLine($"### [{path}]");
            sb.AppendLine(content);
            sb.AppendLine();
        }

        return sb.Length == 0 ? "(none)" : sb.ToString();
    }

    private static List<string> ParseUpdateSelectionResponse(string raw)
    {
        var json = raw.Trim();
        if (json.StartsWith("```", StringComparison.Ordinal))
        {
            var start = json.IndexOf('\n') + 1;
            var end = json.LastIndexOf("```", StringComparison.Ordinal);
            if (start > 0 && end > start) json = json[start..end].Trim();
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<UpdateSelectionResponse>(json, JsonOpts);
            return parsed?.UpdateCandidates?
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToList() ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static string? NormalizeRelativePath(string path)
    {
        var normalized = path.Trim().Trim('`', '"', '\'').Replace('\\', '/');
        if (!normalized.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            return null;
        if (!normalized.StartsWith("pages/", StringComparison.OrdinalIgnoreCase))
            return null;
        if (normalized.Contains("..", StringComparison.Ordinal))
            return null;
        return normalized;
    }

    // ---- ソース読み取り ----

    private static async Task<string> ReadSourceContent(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".md" or ".txt" => await File.ReadAllTextAsync(filePath, Encoding.UTF8),
            ".pdf"          => $"[PDF file: {Path.GetFileName(filePath)} — text extraction not yet supported. Please convert to .md or .txt first.]",
            ".docx"         => $"[DOCX file: {Path.GetFileName(filePath)} — text extraction not yet supported. Please convert to .md or .txt first.]",
            _               => await File.ReadAllTextAsync(filePath, Encoding.UTF8)
        };
    }

    // ---- JSON パース ----

    private static IngestLlmResponse? ParseLlmResponse(string raw)
    {
        // コードフェンスを除去
        var json = raw.Trim();
        if (json.StartsWith("```"))
        {
            var start = json.IndexOf('\n') + 1;
            var end   = json.LastIndexOf("```");
            if (end > start) json = json[start..end].Trim();
        }

        try
        {
            return JsonSerializer.Deserialize<IngestLlmResponse>(json, JsonOpts);
        }
        catch
        {
            return null;
        }
    }

    private static IngestResult Fail(string msg) => new() { Success = false, ErrorMessage = msg };

    private static IReadOnlyList<string> CollectExistingTags(string wikiRoot, IReadOnlyList<WikiPageItem> pages)
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var page in pages.Where(p => !p.IsRoot))
        {
            var fullPath = Path.Combine(wikiRoot, page.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath)) continue;

            var content = File.ReadAllText(fullPath, Encoding.UTF8);
            foreach (var tag in ExtractFrontmatterTags(content))
                tags.Add(tag);
        }

        return tags.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string BuildTagVocabulary(IReadOnlyList<string> tags)
        => tags.Count == 0 ? "(none)" : string.Join(", ", tags);

    private static void CanonicalizeResultTags(IngestLlmResponse resp, IReadOnlyList<string> existingTags)
    {
        var canonicalByKey = BuildCanonicalTagMap(existingTags);

        foreach (var p in resp.NewPages)
        {
            if (string.IsNullOrWhiteSpace(p.Content)) continue;
            p.Content = CanonicalizeFrontmatterTags(p.Content, canonicalByKey);
        }

        foreach (var u in resp.UpdatedPages)
        {
            if (string.IsNullOrWhiteSpace(u.Diff)) continue;
            u.Diff = CanonicalizeFrontmatterTags(u.Diff, canonicalByKey);
        }
    }

    private static Dictionary<string, string> BuildCanonicalTagMap(IReadOnlyList<string> existingTags)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in existingTags)
        {
            var normalized = NormalizeTagToken(tag);
            if (string.IsNullOrWhiteSpace(normalized)) continue;

            var key = TagMatchKey(normalized);
            if (!map.ContainsKey(key))
                map[key] = normalized;
        }
        return map;
    }

    private static string CanonicalizeFrontmatterTags(string markdown, Dictionary<string, string> canonicalByKey)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            markdown,
            @"(?ms)\A---\s*\r?\n(?<fm>.*?)(?:\r?\n)---");
        if (!match.Success) return markdown;

        var frontmatter = match.Groups["fm"].Value;
        var lines = frontmatter.Split('\n').ToArray();
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();
            if (!trimmed.StartsWith("tags:", StringComparison.OrdinalIgnoreCase))
                continue;

            var tags = ParseTagsLine(trimmed);
            if (tags.Count == 0) continue;

            var canonical = tags
                .Select(t =>
                {
                    var normalized = NormalizeTagToken(t);
                    var key = TagMatchKey(normalized);
                    return canonicalByKey.TryGetValue(key, out var existing) ? existing : normalized;
                })
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
                .ToList();

            lines[i] = $"tags: [{string.Join(", ", canonical)}]";
            break;
        }

        var newFrontmatter = string.Join('\n', lines);
        return markdown.Substring(0, match.Groups["fm"].Index)
               + newFrontmatter
               + markdown.Substring(match.Groups["fm"].Index + match.Groups["fm"].Length);
    }

    private static List<string> ExtractFrontmatterTags(string markdown)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            markdown,
            @"(?ms)\A---\s*\r?\n(?<fm>.*?)(?:\r?\n)---");
        if (!match.Success) return [];

        var lines = match.Groups["fm"].Value.Split('\n');
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (!line.StartsWith("tags:", StringComparison.OrdinalIgnoreCase))
                continue;
            return ParseTagsLine(line);
        }

        return [];
    }

    private static List<string> ParseTagsLine(string line)
    {
        var idx = line.IndexOf(':');
        if (idx < 0) return [];
        var value = line[(idx + 1)..].Trim();
        if (!value.StartsWith("[", StringComparison.Ordinal) || !value.EndsWith("]", StringComparison.Ordinal))
            return [];

        var inner = value[1..^1];
        return inner.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.Trim().Trim('"', '\''))
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();
    }

    private static string NormalizeTagToken(string tag)
    {
        var t = tag.Trim().Trim('"', '\'').ToLowerInvariant();
        t = t.Replace('_', '-').Replace(' ', '-');
        t = System.Text.RegularExpressions.Regex.Replace(t, @"-+", "-");
        t = System.Text.RegularExpressions.Regex.Replace(t, @"[^a-z0-9\-]", "");
        return t.Trim('-');
    }

    private static string TagMatchKey(string normalizedTag)
    {
        var key = normalizedTag;
        if (key.EndsWith("ies", StringComparison.Ordinal) && key.Length > 4)
            key = key[..^3] + "y";
        else if (key.EndsWith("s", StringComparison.Ordinal) && !key.EndsWith("ss", StringComparison.Ordinal) && key.Length > 3)
            key = key[..^1];
        return key;
    }

    private sealed class UpdateSelectionResponse
    {
        public List<string> UpdateCandidates { get; set; } = [];
    }
}
