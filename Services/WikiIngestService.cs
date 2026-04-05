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
        try
        {
            // 1. raw/ にコピー
            progress?.Report("Copying source to raw/...");
            var rawPath = await _wiki.AddSource(wikiRoot, sourceFilePath);

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

            var systemPrompt = BuildSystemPrompt(schema);
            var userPrompt   = BuildUserPrompt(index, sourceContent, Path.GetFileName(sourceFilePath));

            // 4. LLM 呼び出し
            progress?.Report("Calling LLM...");
            var raw = await _llm.ChatCompletionAsync(systemPrompt, userPrompt, cancellationToken);

            // 5. JSON パース
            progress?.Report("Parsing LLM response...");
            var llmResp = ParseLlmResponse(raw);
            if (llmResp == null)
                return Fail("Failed to parse LLM response as JSON.");

            // 6. ファイルシステムに反映
            progress?.Report("Writing pages...");
            foreach (var p in llmResp.NewPages)
            {
                if (!string.IsNullOrWhiteSpace(p.Path) && !string.IsNullOrWhiteSpace(p.Content))
                    await _wiki.SavePage(wikiRoot, p.Path, p.Content);
            }

            // 更新ページ: diff ではなく全文として扱う（LLM が全文を返す想定）
            foreach (var u in llmResp.UpdatedPages)
            {
                if (!string.IsNullOrWhiteSpace(u.Path) && !string.IsNullOrWhiteSpace(u.Diff))
                    await _wiki.SavePage(wikiRoot, u.Path, u.Diff);
            }

            // 7. index.md 更新
            if (!string.IsNullOrWhiteSpace(llmResp.IndexUpdate))
                await _wiki.UpdateIndex(wikiRoot, llmResp.IndexUpdate);

            // 8. log.md 追記
            if (!string.IsNullOrWhiteSpace(llmResp.LogEntry))
                await _wiki.AppendLog(wikiRoot, llmResp.LogEntry);

            // 9. メタ更新
            await _wiki.UpdateMeta(wikiRoot, m =>
            {
                m.Stats.LastIngest = DateTime.UtcNow;
                m.Stats.TotalSources = _wiki.GetAllSources(wikiRoot).Count;
                m.Stats.TotalPages   = _wiki.GetAllPages(wikiRoot).Count(p => !p.IsRoot);
            });

            progress?.Report("Done.");

            return new IngestResult
            {
                Success = true,
                Summary = llmResp.Summary,
                NewPages = llmResp.NewPages,
                UpdatedPages = llmResp.UpdatedPages,
                IndexUpdate = llmResp.IndexUpdate,
                LogEntry = llmResp.LogEntry
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

    private static string BuildUserPrompt(string index, string sourceContent, string fileName) => $"""
Please ingest the following source into the Wiki.

## Current index.md
{index}

## Source File: {fileName}
{sourceContent}

## Instructions
1. Create a summary page in sources/ for this source
2. List any existing pages that need updating with their full updated content
3. Create new entity/concept pages if needed
4. Generate the full updated index.md
5. Generate a log.md entry for this ingest

Respond with JSON only (no markdown code fences).
""";

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
}
