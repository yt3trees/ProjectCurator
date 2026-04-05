using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Curia.Models;

namespace Curia.Services;

/// <summary>
/// Wiki ディレクトリに対するファイル I/O 操作をすべてここに集約する。
/// </summary>
public class WikiService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    // ---- パス解決 ----

    public static string GetWikiRoot(string projectContextPath, string? domain = null)
    {
        var baseWiki = Path.Combine(projectContextPath, "wiki");
        if (string.IsNullOrEmpty(domain)) return baseWiki;
        return Path.Combine(baseWiki, domain);
    }

    public static List<string> GetDomains(string projectContextPath)
    {
        var baseWiki = Path.Combine(projectContextPath, "wiki");
        if (!Directory.Exists(baseWiki)) return [];

        return Directory.EnumerateDirectories(baseWiki)
            .Select(Path.GetFileName)
            .Where(n => !string.IsNullOrEmpty(n))
            .Cast<string>()
            .ToList();
    }

    public static string GetPagesDir(string wikiRoot)  => Path.Combine(wikiRoot, "pages");
    public static string GetRawDir(string wikiRoot)    => Path.Combine(wikiRoot, "raw");
    public static string GetIndexPath(string wikiRoot) => Path.Combine(wikiRoot, "index.md");
    public static string GetLogPath(string wikiRoot)   => Path.Combine(wikiRoot, "log.md");
    public static string GetSchemaPath(string wikiRoot)=> Path.Combine(wikiRoot, "wiki-schema.md");
    public static string GetMetaPath(string wikiRoot)  => Path.Combine(wikiRoot, ".wiki-meta.json");

    // ---- 初期化 ----

    public async Task InitializeWiki(string projectContextPath, string projectName, string domain)
    {
        var wikiRoot = GetWikiRoot(projectContextPath, domain);

        // ディレクトリ作成
        Directory.CreateDirectory(wikiRoot);
        Directory.CreateDirectory(GetRawDir(wikiRoot));
        var pagesDir = GetPagesDir(wikiRoot);
        Directory.CreateDirectory(Path.Combine(pagesDir, "sources"));
        Directory.CreateDirectory(Path.Combine(pagesDir, "entities"));
        Directory.CreateDirectory(Path.Combine(pagesDir, "concepts"));
        Directory.CreateDirectory(Path.Combine(pagesDir, "analysis"));

        // wiki-schema.md
        if (!File.Exists(GetSchemaPath(wikiRoot)))
            await File.WriteAllTextAsync(GetSchemaPath(wikiRoot), BuildSchemaTemplate(projectName, domain), Encoding.UTF8);

        // Wiki-local AGENTS.md / CLAUDE.md
        var wikiAgentsPath = Path.Combine(wikiRoot, "AGENTS.md");
        if (!File.Exists(wikiAgentsPath))
            await File.WriteAllTextAsync(wikiAgentsPath, BuildWikiAgentsTemplate(projectName, domain), Encoding.UTF8);

        var wikiClaudePath = Path.Combine(wikiRoot, "CLAUDE.md");
        if (!File.Exists(wikiClaudePath))
            await File.WriteAllTextAsync(wikiClaudePath, "@AGENTS.md\n", Encoding.UTF8);

        // index.md
        if (!File.Exists(GetIndexPath(wikiRoot)))
            await File.WriteAllTextAsync(GetIndexPath(wikiRoot), BuildInitialIndex(), Encoding.UTF8);

        // log.md
        if (!File.Exists(GetLogPath(wikiRoot)))
        {
            var entry = $"# Wiki Log\n\n## [{DateTime.Now:yyyy-MM-dd}] init | Wiki 作成\n- プロジェクト: {projectName}\n";
            await File.WriteAllTextAsync(GetLogPath(wikiRoot), entry, Encoding.UTF8);
        }

        // .wiki-meta.json
        if (!File.Exists(GetMetaPath(wikiRoot)))
        {
            var meta = new WikiMeta { Created = DateTime.UtcNow };
            await SaveMeta(wikiRoot, meta);
        }
    }

    // ---- ページ CRUD ----

    public IReadOnlyList<WikiPageItem> GetAllPages(string wikiRoot)
    {
        var result = new List<WikiPageItem>();

        // ルートファイル (index.md / log.md)
        foreach (var path in new[] { GetIndexPath(wikiRoot), GetLogPath(wikiRoot) })
        {
            if (!File.Exists(path)) continue;
            result.Add(new WikiPageItem
            {
                Title = Path.GetFileNameWithoutExtension(path),
                RelativePath = Path.GetFileName(path),
                Category = "root",
                LastModified = File.GetLastWriteTime(path),
                IsRoot = true
            });
        }

        // pages/ 配下
        var pagesDir = GetPagesDir(wikiRoot);
        if (!Directory.Exists(pagesDir)) return result;

        foreach (var file in Directory.EnumerateFiles(pagesDir, "*.md", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(wikiRoot, file).Replace('\\', '/');
            var category = GetCategoryFromPath(rel);
            result.Add(new WikiPageItem
            {
                Title = ExtractTitleFromFile(file),
                RelativePath = rel,
                Category = category,
                LastModified = File.GetLastWriteTime(file),
                IsRoot = false
            });
        }

        return result;
    }

    public WikiPageItem? GetPage(string wikiRoot, string relativePath)
    {
        var fullPath = Path.Combine(wikiRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath)) return null;

        return new WikiPageItem
        {
            Title = ExtractTitleFromFile(fullPath),
            RelativePath = relativePath,
            Category = GetCategoryFromPath(relativePath),
            Content = File.ReadAllText(fullPath, Encoding.UTF8),
            LastModified = File.GetLastWriteTime(fullPath),
            IsRoot = relativePath is "index.md" or "log.md"
        };
    }

    public async Task SavePage(string wikiRoot, string relativePath, string content)
    {
        var fullPath = Path.Combine(wikiRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(fullPath, content, Encoding.UTF8);
    }

    public void DeletePage(string wikiRoot, string relativePath)
    {
        var fullPath = Path.Combine(wikiRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(fullPath)) File.Delete(fullPath);
    }

    // ---- ソース管理 ----

    public async Task<string> AddSource(string wikiRoot, string sourceFilePath)
    {
        var rawDir = GetRawDir(wikiRoot);
        Directory.CreateDirectory(rawDir);
        var destPath = Path.Combine(rawDir, Path.GetFileName(sourceFilePath));

        // 同名ファイルがある場合はタイムスタンプを付与
        if (File.Exists(destPath))
        {
            var stem = Path.GetFileNameWithoutExtension(sourceFilePath);
            var ext  = Path.GetExtension(sourceFilePath);
            destPath = Path.Combine(rawDir, $"{stem}_{DateTime.Now:yyyyMMddHHmmss}{ext}");
        }

        await Task.Run(() => File.Copy(sourceFilePath, destPath));
        return destPath;
    }

    public IReadOnlyList<WikiSourceItem> GetAllSources(string wikiRoot)
    {
        var rawDir = GetRawDir(wikiRoot);
        if (!Directory.Exists(rawDir)) return [];

        return Directory.EnumerateFiles(rawDir, "*", SearchOption.TopDirectoryOnly)
            .Select(f => new WikiSourceItem
            {
                FileName = Path.GetFileName(f),
                RelativePath = Path.GetRelativePath(wikiRoot, f).Replace('\\', '/'),
                FullPath = f,
                AddedAt = File.GetCreationTime(f),
                FileSizeBytes = new FileInfo(f).Length
            })
            .OrderByDescending(s => s.AddedAt)
            .ToList();
    }

    // ---- メタデータ ----

    public WikiMeta GetMeta(string wikiRoot)
    {
        var path = GetMetaPath(wikiRoot);
        if (!File.Exists(path)) return new WikiMeta { Created = DateTime.UtcNow };
        try
        {
            var json = File.ReadAllText(path, Encoding.UTF8);
            return JsonSerializer.Deserialize<WikiMeta>(json, JsonOpts) ?? new WikiMeta { Created = DateTime.UtcNow };
        }
        catch { return new WikiMeta { Created = DateTime.UtcNow }; }
    }

    public async Task UpdateMeta(string wikiRoot, Action<WikiMeta> update)
    {
        var meta = GetMeta(wikiRoot);
        update(meta);
        await SaveMeta(wikiRoot, meta);
    }

    private async Task SaveMeta(string wikiRoot, WikiMeta meta)
    {
        var json = JsonSerializer.Serialize(meta, JsonOpts);
        await File.WriteAllTextAsync(GetMetaPath(wikiRoot), json, Encoding.UTF8);
    }

    // ---- Index / Log ----

    public string GetIndex(string wikiRoot)
    {
        var path = GetIndexPath(wikiRoot);
        return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : "";
    }

    public async Task UpdateIndex(string wikiRoot, string newContent)
        => await File.WriteAllTextAsync(GetIndexPath(wikiRoot), newContent, Encoding.UTF8);

    public async Task AppendLog(string wikiRoot, string entry)
    {
        var path = GetLogPath(wikiRoot);
        var existing = File.Exists(path) ? await File.ReadAllTextAsync(path, Encoding.UTF8) : "# Wiki Log\n";
        await File.WriteAllTextAsync(path, existing + "\n" + entry, Encoding.UTF8);
    }

    // ---- ヘルパー ----

    private static string GetCategoryFromPath(string relativePath)
    {
        if (relativePath.StartsWith("pages/sources/", StringComparison.OrdinalIgnoreCase)) return "sources";
        if (relativePath.StartsWith("pages/entities/", StringComparison.OrdinalIgnoreCase)) return "entities";
        if (relativePath.StartsWith("pages/concepts/", StringComparison.OrdinalIgnoreCase)) return "concepts";
        if (relativePath.StartsWith("pages/analysis/", StringComparison.OrdinalIgnoreCase)) return "analysis";
        return "root";
    }

    private static string ExtractTitleFromFile(string filePath)
    {
        try
        {
            foreach (var line in File.ReadLines(filePath, Encoding.UTF8))
            {
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("title:", StringComparison.OrdinalIgnoreCase))
                    return trimmed["title:".Length..].Trim().Trim('"');
                if (trimmed.StartsWith('#'))
                    return trimmed.TrimStart('#').Trim();
            }
        }
        catch { /* ignore */ }
        return Path.GetFileNameWithoutExtension(filePath);
    }

    private static string BuildSchemaTemplate(string projectName, string domain) => $"""
# Wiki Schema

## Project Info
- Project: {projectName}
- Domain: {domain}

## Page Conventions
- File names: kebab-case
- Frontmatter: title, created, updated, sources, tags
- Wikilink format: [[Page Name]]
- Each page begins with a TLDR (3 lines max)

## Category Definitions
- sources/: Source document summaries. One source = one page.
- entities/: Concrete objects (tables, screens, APIs, forms, user roles, etc.)
- concepts/: Design ideas, business rules, workflows, technical policies
- analysis/: Comparative analyses, Q&A answers, research results

## Ingest Workflow
1. Read source and extract key insights
2. Create summary page in sources/
3. Update index.md
4. Update or create related entities/ and concepts/ pages
5. Append entry to log.md

## Lint Rules
- Contradiction: Flag different descriptions of the same fact
- Stale: Flag old descriptions overwritten by new sources
- Orphan: Flag pages with 0 inbound links
- Missing: Suggest pages for topics mentioned in 3+ pages but not yet created
""";

    private static string BuildWikiAgentsTemplate(string projectName, string domain) => $"""
# Wiki Agent Guide ({projectName})

This AGENTS.md governs edits under this `wiki/` directory only.

## Role

You are a wiki maintainer. Keep the wiki concise, linked, and accumulative.
Prefer updating existing pages over creating duplicates.

## Scope

- Project: {projectName}
- Domain: {domain}
- Managed tree:
  - `pages/sources/`
  - `pages/entities/`
  - `pages/concepts/`
  - `pages/analysis/`
  - `index.md`
  - `log.md`
  - `.wiki-meta.json`
- Do not rewrite or mutate source snapshots in `raw/`.

## Operating Rules

1. Treat `raw/` as immutable source-of-truth snapshots.
2. Keep pages in Markdown with YAML frontmatter:
   - `title`, `created`, `updated`, `sources`, `tags`
3. Use wikilinks for cross references: `[[Page Name]]`.
4. Keep one topic per page; avoid near-duplicate pages.
5. Preserve provenance:
   - Every factual addition should be traceable to `sources`.
   - Distinguish facts vs. inference clearly.
6. Keep `index.md` synchronized with created/renamed/removed pages.
7. Append concise operation notes to `log.md` after meaningful changes.

## Ingest and Update Policy

- Ingest only reusable project knowledge.
- Exclude personal scratch notes, temporary link dumps, and private thoughts.
- Prefer incremental edits to maintain continuity of the knowledge graph.

## Lint Expectations

- BrokenLink: none
- MissingSource: none
- Orphan/Stale/Missing pages: acceptable only with explicit rationale

## Response Style for Agents

- Make minimal, reversible edits.
- Report touched files and why each changed.
- If confidence is low, ask for one clarification before large structural changes.
""";

    private static string BuildInitialIndex() => """
# Wiki Index

## Sources (0)
| Page | Summary | Source Date | Tags |
|------|---------|-------------|------|

## Entities (0)
| Page | Summary | Sources | Tags |
|------|---------|---------|------|

## Concepts (0)
| Page | Summary | Sources | Tags |
|------|---------|---------|------|

## Analysis (0)
| Page | Summary | Created |
|------|---------|---------|
""";
}
