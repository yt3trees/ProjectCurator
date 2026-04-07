using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Curia.Models;

namespace Curia.Services;

/// <summary>
/// Wiki ディレクトリに対するファイル I/O 操作をすべてここに集約する。
/// </summary>
public class WikiDomainLockException : Exception
{
    public WikiDomainLockException(string wikiRoot)
        : base($"Could not acquire domain lock for: {wikiRoot}. Another operation may be in progress.") { }
}

public class WikiService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly string[] DefaultCategories = ["sources", "entities", "concepts", "analysis"];

    // Windows reserved names
    private static readonly HashSet<string> WindowsReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON","PRN","AUX","NUL",
        "COM1","COM2","COM3","COM4","COM5","COM6","COM7","COM8","COM9",
        "LPT1","LPT2","LPT3","LPT4","LPT5","LPT6","LPT7","LPT8","LPT9"
    };

    private static readonly char[] WindowsInvalidChars = ['<', '>', ':', '"', '|', '?', '*'];

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
    public static string GetMetaPath(string wikiRoot)              => Path.Combine(wikiRoot, ".wiki-meta.json");
    public static string GetQueryHistoryDir(string wikiRoot)       => Path.Combine(wikiRoot, "query_history");
    public static string GetCategoriesConfigPath(string wikiRoot)  => Path.Combine(wikiRoot, ".wiki-categories.json");
    public static string GetPromptsConfigPath(string wikiRoot)     => Path.Combine(wikiRoot, ".wiki-prompts.json");
    public static string GetTxnPath(string wikiRoot)               => Path.Combine(wikiRoot, ".wiki-txn.json");
    public static string GetRenameTxnPath(string wikiRoot)         => Path.Combine(wikiRoot, ".wiki-rename-txn.json");

    // ---- 初期化 ----

    public async Task InitializeWiki(string projectContextPath, string projectName, string domain)
    {
        var wikiRoot = GetWikiRoot(projectContextPath, domain);

        // ディレクトリ作成
        Directory.CreateDirectory(wikiRoot);
        Directory.CreateDirectory(GetRawDir(wikiRoot));
        var pagesDir = GetPagesDir(wikiRoot);
        foreach (var cat in DefaultCategories)
            Directory.CreateDirectory(Path.Combine(pagesDir, cat));

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

        // .wiki-categories.json
        if (!File.Exists(GetCategoriesConfigPath(wikiRoot)))
            await SaveCategoriesAtomicAsync(wikiRoot, new WikiCategoriesConfig());

        // .wiki-prompts.json
        if (!File.Exists(GetPromptsConfigPath(wikiRoot)))
            await SavePromptsAtomicAsync(wikiRoot, new WikiPromptConfig());
    }

    // ---- ページ CRUD ----

    public IReadOnlyList<WikiPageItem> GetAllPages(string wikiRoot)
    {
        var result = new List<WikiPageItem>();
        var categoriesConfig = LoadCategories(wikiRoot);

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
            var category = GetCategoryFromPath(rel, categoriesConfig.Categories);
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

        var categoriesConfig = LoadCategories(wikiRoot);
        return new WikiPageItem
        {
            Title = ExtractTitleFromFile(fullPath),
            RelativePath = relativePath,
            Category = GetCategoryFromPath(relativePath, categoriesConfig.Categories),
            Content = File.ReadAllText(fullPath, Encoding.UTF8),
            LastModified = File.GetLastWriteTime(fullPath),
            IsRoot = relativePath is "index.md" or "log.md"
        };
    }

    /// <summary>
    /// ページを保存する。categories が null の場合は自動ロード。
    /// index.md / log.md はバリデーションをスキップする。
    /// </summary>
    public async Task SavePage(
        string wikiRoot,
        string relativePath,
        string content,
        IReadOnlyList<string>? validCategories = null)
    {
        // index.md / log.md は直接保存（専用経路）
        if (relativePath is "index.md" or "log.md")
        {
            var rootFull = Path.Combine(wikiRoot, relativePath);
            await WriteFileAtomicAsync(rootFull, content);
            return;
        }

        var cats = validCategories ?? LoadCategories(wikiRoot).Categories;
        var validation = ValidatePagePath(wikiRoot, relativePath, cats);
        if (!validation.IsValid)
            throw new InvalidOperationException($"Save rejected: {validation.ErrorReason}");

        var normalizedRel = NormalizeRelativePath(relativePath);
        var fullPath = Path.Combine(wikiRoot, normalizedRel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await WriteFileAtomicAsync(fullPath, content);
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

    /// <summary>相対パスから論理カテゴリ名を取得する（動的カテゴリ対応）。</summary>
    public static string GetCategoryFromPath(string relativePath, IReadOnlyList<string> categories)
    {
        // pages/<category>/ プレフィックスからカテゴリを抽出
        if (!relativePath.StartsWith("pages/", StringComparison.OrdinalIgnoreCase))
            return "root";

        var afterPages = relativePath["pages/".Length..];
        var slashIdx = afterPages.IndexOf('/');
        if (slashIdx < 0) return "root";

        var segment = afterPages[..slashIdx].ToLowerInvariant();
        if (categories.Any(c => c.Equals(segment, StringComparison.OrdinalIgnoreCase)))
            return segment;

        // カテゴリ定義にないが pages/ 配下のディレクトリ → undefined カテゴリとして返す
        return segment;
    }

    /// <summary>パス正規化: バックスラッシュ → スラッシュ、先頭末尾トリム。</summary>
    public static string NormalizeRelativePath(string path)
        => path.Replace('\\', '/').Trim();

    /// <summary>ファイルを一時ファイル経由で原子的に書き込む。</summary>
    public static async Task WriteFileAtomicAsync(string targetPath, string content)
    {
        var dir = Path.GetDirectoryName(targetPath)!;
        Directory.CreateDirectory(dir);
        var tmp = Path.Combine(dir, $"{Path.GetFileName(targetPath)}.tmp-{Guid.NewGuid():N}");
        try
        {
            await File.WriteAllTextAsync(tmp, content, Encoding.UTF8);
            if (File.Exists(targetPath))
                File.Replace(tmp, targetPath, null);
            else
                File.Move(tmp, targetPath);
        }
        catch
        {
            try { File.Delete(tmp); } catch { /* ignore */ }
            throw;
        }
    }

    /// <summary>設定ファイルを一時ファイル経由で原子的に書き込む（JSON）。</summary>
    private static async Task WriteJsonAtomicAsync<T>(string targetPath, T obj)
    {
        var json = JsonSerializer.Serialize(obj, JsonOpts);
        await WriteFileAtomicAsync(targetPath, json);
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
<!-- CURIA:CATEGORIES:BEGIN -->
- `pages/sources/`
- `pages/entities/`
- `pages/concepts/`
- `pages/analysis/`
<!-- CURIA:CATEGORIES:END -->
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

    // ---- カテゴリ設定 ----

    /// <summary>
    /// .wiki-categories.json を読み込む。
    /// 未存在時はデフォルト4カテゴリを返す（ファイル生成しない）。
    /// </summary>
    public WikiCategoriesConfig LoadCategories(string wikiRoot)
    {
        var path = GetCategoriesConfigPath(wikiRoot);
        if (!File.Exists(path))
        {
            var def = new WikiCategoriesConfig();
            // pages/ 配下の既存ディレクトリも取り込む（初回アクセス時）
            MergeExistingDirs(wikiRoot, def);
            return def;
        }

        // I/O リトライ
        string? json = null;
        for (int i = 0; i < 3; i++)
        {
            try { json = File.ReadAllText(path, Encoding.UTF8); break; }
            catch (IOException) when (i < 2) { Thread.Sleep(200 * (i + 1)); }
            catch (IOException)
            {
                return new WikiCategoriesConfig { HasNamingConflict = true, ConflictDetail = "I/O error reading .wiki-categories.json" };
            }
        }

        WikiCategoriesConfig? cfg = null;
        try { cfg = JsonSerializer.Deserialize<WikiCategoriesConfig>(json!, JsonOpts); }
        catch
        {
            // パース失敗: 退避してデフォルト再生成
            RecoverBrokenConfigFile(path);
            var recovered = new WikiCategoriesConfig();
            MergeExistingDirs(wikiRoot, recovered);
            return recovered;
        }

        if (cfg == null) { cfg = new WikiCategoriesConfig(); MergeExistingDirs(wikiRoot, cfg); return cfg; }
        if (cfg.Version != 1) { cfg.IsUnknownVersion = true; return cfg; }

        // 正規化
        cfg.Categories = cfg.Categories.Select(c => c.ToLowerInvariant()).ToList();

        // 重複チェック
        var dups = cfg.Categories.GroupBy(c => c, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (dups.Count > 0) { cfg.HasNamingConflict = true; cfg.ConflictDetail = $"Duplicate categories: {string.Join(", ", dups)}"; }

        return cfg;
    }

    private static void MergeExistingDirs(string wikiRoot, WikiCategoriesConfig cfg)
    {
        var pagesDir = GetPagesDir(wikiRoot);
        if (!Directory.Exists(pagesDir)) return;

        var existing = Directory.EnumerateDirectories(pagesDir, "*", SearchOption.TopDirectoryOnly)
            .Select(d => Path.GetFileName(d)!.ToLowerInvariant())
            .Where(n => !string.IsNullOrEmpty(n))
            .ToList();

        // 重複（大文字小文字込み）確認
        var normalized = existing.GroupBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        var conflicts = normalized.Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (conflicts.Count > 0)
        {
            cfg.HasNamingConflict = true;
            cfg.ConflictDetail = $"Case-insensitive duplicate directories detected: {string.Join(", ", conflicts)}";
            return;
        }

        foreach (var dir in existing)
        {
            if (!cfg.Categories.Any(c => c.Equals(dir, StringComparison.OrdinalIgnoreCase)))
                cfg.Categories.Add(dir);
        }
    }

    private static void RecoverBrokenConfigFile(string path)
    {
        try
        {
            var broken = $"{path}.broken-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.json";
            File.Move(path, broken);
        }
        catch { /* 退避失敗時は無視 */ }
    }

    public async Task SaveCategoriesAtomicAsync(string wikiRoot, WikiCategoriesConfig config)
    {
        // sources は必ず先頭に保持
        var cats = config.Categories.Select(c => c.ToLowerInvariant()).ToList();
        if (!cats.Contains("sources", StringComparer.OrdinalIgnoreCase))
            cats.Insert(0, "sources");
        config.Categories = cats;
        await WriteJsonAtomicAsync(GetCategoriesConfigPath(wikiRoot), config);
    }

    public async Task<(bool Success, string? Error)> AddCategoryAsync(string wikiRoot, string name)
    {
        var normalized = name.ToLowerInvariant().Trim();
        var nameErr = ValidateCategoryName(normalized);
        if (nameErr != null) return (false, nameErr);

        var cfg = LoadCategories(wikiRoot);
        if (cfg.HasNamingConflict) return (false, cfg.ConflictDetail);
        if (cfg.Categories.Any(c => c.Equals(normalized, StringComparison.OrdinalIgnoreCase)))
            return (false, $"Category '{normalized}' already exists.");

        cfg.Categories.Add(normalized);
        await SaveCategoriesAtomicAsync(wikiRoot, cfg);
        await UpdateAgentsMdCategoryBlockAsync(wikiRoot, cfg.Categories);
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> DeleteCategoryAsync(string wikiRoot, string name)
    {
        var normalized = name.ToLowerInvariant().Trim();
        if (normalized == "sources") return (false, "The 'sources' category cannot be deleted.");

        var cfg = LoadCategories(wikiRoot);
        if (cfg.HasNamingConflict) return (false, cfg.ConflictDetail);

        var removed = cfg.Categories.RemoveAll(c => c.Equals(normalized, StringComparison.OrdinalIgnoreCase));
        if (removed == 0) return (false, $"Category '{normalized}' not found.");

        await SaveCategoriesAtomicAsync(wikiRoot, cfg);
        await UpdateAgentsMdCategoryBlockAsync(wikiRoot, cfg.Categories);
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> RenameCategoryAsync(
        string wikiRoot, string oldName, string newName,
        CancellationToken cancellationToken = default)
    {
        var oldNorm = oldName.ToLowerInvariant().Trim();
        var newNorm = newName.ToLowerInvariant().Trim();

        if (oldNorm == "sources") return (false, "The 'sources' category cannot be renamed.");
        if (oldNorm == newNorm) return (false, "Old and new category names are the same.");

        var nameErr = ValidateCategoryName(newNorm);
        if (nameErr != null) return (false, nameErr);

        var cfg = LoadCategories(wikiRoot);
        if (cfg.HasNamingConflict) return (false, cfg.ConflictDetail);
        if (!cfg.Categories.Any(c => c.Equals(oldNorm, StringComparison.OrdinalIgnoreCase)))
            return (false, $"Category '{oldNorm}' not found.");
        if (cfg.Categories.Any(c => c.Equals(newNorm, StringComparison.OrdinalIgnoreCase)))
            return (false, $"Category '{newNorm}' already exists.");

        var oldPath = Path.Combine(GetPagesDir(wikiRoot), oldNorm);
        var newPath = Path.Combine(GetPagesDir(wikiRoot), newNorm);
        var tempPath = Path.Combine(GetPagesDir(wikiRoot), $".{oldNorm}.rename-tmp-{Guid.NewGuid():N}");
        var oldExisted = Directory.Exists(oldPath);

        // ジャーナル作成
        var txn = new WikiRenameTxn
        {
            OldCategory = oldNorm, NewCategory = newNorm,
            OldPath = oldPath, NewPath = newPath, TempPath = tempPath,
            OldExistedAtStart = oldExisted,
            Phase = WikiRenameTxnPhase.Prepared
        };
        await WriteJsonAtomicAsync(GetRenameTxnPath(wikiRoot), txn);

        if (!oldExisted)
        {
            // ディレクトリなし: 設定のみ変更
            var idx = cfg.Categories.FindIndex(c => c.Equals(oldNorm, StringComparison.OrdinalIgnoreCase));
            cfg.Categories[idx] = newNorm;
            txn.Phase = WikiRenameTxnPhase.UpdatingConfig;
            await WriteJsonAtomicAsync(GetRenameTxnPath(wikiRoot), txn);
            try
            {
                await SaveCategoriesAtomicAsync(wikiRoot, cfg);
                txn.Phase = WikiRenameTxnPhase.Committed;
                await WriteJsonAtomicAsync(GetRenameTxnPath(wikiRoot), txn);
                await UpdateAgentsMdCategoryBlockAsync(wikiRoot, cfg.Categories);
                File.Delete(GetRenameTxnPath(wikiRoot));
                return (true, null);
            }
            catch (Exception ex)
            {
                txn.Phase = WikiRenameTxnPhase.RolledBack;
                await WriteJsonAtomicAsync(GetRenameTxnPath(wikiRoot), txn);
                File.Delete(GetRenameTxnPath(wikiRoot));
                return (false, $"Failed to update config: {ex.Message}");
            }
        }

        // ディレクトリあり: 2段階移動
        if (Directory.Exists(newPath))
            return (false, $"Directory '{newNorm}' already exists. Cannot merge automatically.");

        try
        {
            txn.Phase = WikiRenameTxnPhase.MovingToTemp;
            await WriteJsonAtomicAsync(GetRenameTxnPath(wikiRoot), txn);
            Directory.Move(oldPath, tempPath);

            txn.Phase = WikiRenameTxnPhase.MovingToNew;
            await WriteJsonAtomicAsync(GetRenameTxnPath(wikiRoot), txn);
            Directory.Move(tempPath, newPath);

            // 設定更新
            txn.Phase = WikiRenameTxnPhase.UpdatingConfig;
            await WriteJsonAtomicAsync(GetRenameTxnPath(wikiRoot), txn);
            var idx = cfg.Categories.FindIndex(c => c.Equals(oldNorm, StringComparison.OrdinalIgnoreCase));
            cfg.Categories[idx] = newNorm;
            await SaveCategoriesAtomicAsync(wikiRoot, cfg);

            txn.Phase = WikiRenameTxnPhase.Committed;
            await WriteJsonAtomicAsync(GetRenameTxnPath(wikiRoot), txn);
            await UpdateAgentsMdCategoryBlockAsync(wikiRoot, cfg.Categories);
            File.Delete(GetRenameTxnPath(wikiRoot));
            return (true, null);
        }
        catch (Exception ex)
        {
            // 補償移動
            txn.Phase = WikiRenameTxnPhase.Compensating;
            try { await WriteJsonAtomicAsync(GetRenameTxnPath(wikiRoot), txn); } catch { /* ignore */ }
            try
            {
                if (Directory.Exists(newPath) && !Directory.Exists(oldPath))
                    Directory.Move(newPath, oldPath);
                else if (Directory.Exists(tempPath) && !Directory.Exists(oldPath))
                    Directory.Move(tempPath, oldPath);
            }
            catch { /* best effort */ }
            txn.Phase = WikiRenameTxnPhase.RolledBack;
            try { await WriteJsonAtomicAsync(GetRenameTxnPath(wikiRoot), txn); } catch { /* ignore */ }
            try { File.Delete(GetRenameTxnPath(wikiRoot)); } catch { /* ignore */ }
            return (false, $"Rename failed: {ex.Message}");
        }
    }

    public async Task RecoverPendingRenameAsync(string wikiRoot)
    {
        var txnPath = GetRenameTxnPath(wikiRoot);
        if (!File.Exists(txnPath)) return;

        WikiRenameTxn? txn = null;
        try { txn = JsonSerializer.Deserialize<WikiRenameTxn>(File.ReadAllText(txnPath, Encoding.UTF8), JsonOpts); }
        catch { try { File.Delete(txnPath); } catch { } return; }
        if (txn == null) { try { File.Delete(txnPath); } catch { } return; }

        switch (txn.Phase)
        {
            case WikiRenameTxnPhase.Committed:
            case WikiRenameTxnPhase.RolledBack:
                // 後片付けのみ
                try { File.Delete(txnPath); } catch { }
                if (Directory.Exists(txn.TempPath))
                    try { Directory.Delete(txn.TempPath, true); } catch { }
                return;

            case WikiRenameTxnPhase.Prepared:
            case WikiRenameTxnPhase.MovingToTemp:
            case WikiRenameTxnPhase.MovingToNew:
            case WikiRenameTxnPhase.UpdatingConfig:
            case WikiRenameTxnPhase.Compensating:
                if (!txn.OldExistedAtStart)
                {
                    // 設定名のみ変更ケース: 設定を旧値に戻す
                    var cfg = LoadCategories(wikiRoot);
                    var idx = cfg.Categories.FindIndex(c => c.Equals(txn.NewCategory, StringComparison.OrdinalIgnoreCase));
                    if (idx >= 0) cfg.Categories[idx] = txn.OldCategory;
                    try { await SaveCategoriesAtomicAsync(wikiRoot, cfg); } catch { }
                }
                else
                {
                    // 実ディレクトリ移動のロールバック
                    try
                    {
                        if (Directory.Exists(txn.NewPath) && !Directory.Exists(txn.OldPath))
                            Directory.Move(txn.NewPath, txn.OldPath);
                        else if (Directory.Exists(txn.TempPath) && !Directory.Exists(txn.OldPath))
                            Directory.Move(txn.TempPath, txn.OldPath);
                        if (Directory.Exists(txn.TempPath))
                            Directory.Delete(txn.TempPath, true);
                    }
                    catch { /* best effort */ }
                }
                try { File.Delete(txnPath); } catch { }
                return;
        }
    }

    // ---- パスバリデーション ----

    /// <summary>
    /// pages/&lt;category&gt;/&lt;name&gt;.md の形式検証。全保存経路で共通使用。
    /// </summary>
    public WikiPathValidationResult ValidatePagePath(
        string wikiRoot,
        string relativePath,
        IReadOnlyList<string> validCategories)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return new WikiPathValidationResult(false, "Path cannot be empty.");

        // バックスラッシュ正規化
        var path = relativePath.Replace('\\', '/').Trim();

        // 絶対パス拒否
        if (Path.IsPathRooted(path) || path.StartsWith("//"))
            return new WikiPathValidationResult(false, "Absolute paths are not allowed.");

        // pages/ 以外のルートは専用経路のみ許可
        if (!path.StartsWith("pages/", StringComparison.OrdinalIgnoreCase))
            return new WikiPathValidationResult(false, "Path must start with 'pages/'.");

        // セグメント分解: pages/<category>/<name>.md
        var parts = path.Split('/');
        if (parts.Length != 3)
            return new WikiPathValidationResult(false, "Path must be in the format 'pages/<category>/<name>.md'. Subdirectories within categories are not allowed.");

        var categorySegment = parts[1].ToLowerInvariant();
        var nameSegment = parts[2];

        // カテゴリバリデーション
        if (!validCategories.Any(c => c.Equals(categorySegment, StringComparison.OrdinalIgnoreCase)))
            return new WikiPathValidationResult(false, $"Category '{categorySegment}' is not defined in .wiki-categories.json. Add it first or use a defined category.");

        // 拡張子検証（大文字小文字不問）
        if (!nameSegment.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            return new WikiPathValidationResult(false, "File must have a .md extension.");

        // <name> 部分（拡張子除く）のバリデーション
        var stem = Path.GetFileNameWithoutExtension(nameSegment);
        var nameErr = ValidateFileName(stem);
        if (nameErr != null) return new WikiPathValidationResult(false, nameErr);

        // 実パスレベル検証: pagesRoot 配下確認
        var pagesRoot = Path.GetFullPath(GetPagesDir(wikiRoot));
        if (!pagesRoot.EndsWith(Path.DirectorySeparatorChar))
            pagesRoot += Path.DirectorySeparatorChar;

        // パスを拡張子 .md に正規化した上で絶対パス取得
        var normalizedName = stem + ".md";
        var normalizedRel = $"pages/{categorySegment}/{normalizedName}";
        string fullPath;
        try { fullPath = Path.GetFullPath(Path.Combine(wikiRoot, normalizedRel)); }
        catch (Exception ex) { return new WikiPathValidationResult(false, $"Invalid path: {ex.Message}"); }

        if (!fullPath.StartsWith(pagesRoot, StringComparison.OrdinalIgnoreCase))
            return new WikiPathValidationResult(false, "Path resolves outside the pages directory (possible traversal attack).");

        // Reparse point 検査: wikiRoot/pages/ から <category>/ まで
        var reparseErr = CheckReparsePointWithinPages(wikiRoot, categorySegment);
        if (reparseErr != null) return new WikiPathValidationResult(false, reparseErr);

        return new WikiPathValidationResult(true, null);
    }

    private static string? CheckReparsePointWithinPages(string wikiRoot, string categorySegment)
    {
        // wiki/<domain>/pages/ と wiki/<domain>/pages/<category>/ を確認
        var pagesDir = Path.GetFullPath(GetPagesDir(wikiRoot));
        var catDir   = Path.Combine(pagesDir, categorySegment);

        foreach (var dir in new[] { pagesDir, catDir })
        {
            if (!Directory.Exists(dir)) continue;
            try
            {
                var attrs = File.GetAttributes(dir);
                if (attrs.HasFlag(FileAttributes.ReparsePoint))
                    return $"Reparse point (junction/symlink) detected at '{dir}'. Saving to reparse points under wiki pages is not allowed.";
            }
            catch { /* ignore */ }
        }
        return null;
    }

    private static string? ValidateFileName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "File name cannot be empty.";
        if (name == "." || name == "..") return $"'{name}' is not a valid file name.";
        if (name.IndexOfAny(WindowsInvalidChars) >= 0) return $"File name contains invalid characters: {string.Join(" ", WindowsInvalidChars)}.";
        if (WindowsReservedNames.Contains(name)) return $"'{name}' is a reserved Windows file name.";
        if (name.EndsWith('.') || name.EndsWith(' ')) return "File name cannot end with a period or space.";
        if (name.Any(c => c < 0x20)) return "File name contains control characters.";
        return null;
    }

    private static string? ValidateCategoryName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "Category name cannot be empty.";
        if (name == "." || name == "..") return $"'{name}' is not a valid category name.";
        if (name.Contains('/') || name.Contains('\\')) return "Category name cannot contain path separators.";
        if (name.IndexOfAny(WindowsInvalidChars) >= 0) return $"Category name contains invalid characters.";
        if (WindowsReservedNames.Contains(name)) return $"'{name}' is a reserved Windows name.";
        if (name.EndsWith('.') || name.EndsWith(' ')) return "Category name cannot end with a period or space.";
        if (name.Any(c => c < 0x20)) return "Category name contains control characters.";
        return null;
    }

    // ---- プロンプト設定 ----

    public WikiPromptConfig LoadPrompts(string wikiRoot)
    {
        var path = GetPromptsConfigPath(wikiRoot);
        if (!File.Exists(path)) return new WikiPromptConfig();

        string? json = null;
        for (int i = 0; i < 3; i++)
        {
            try { json = File.ReadAllText(path, Encoding.UTF8); break; }
            catch (IOException) when (i < 2) { Thread.Sleep(200 * (i + 1)); }
            catch (IOException) { return new WikiPromptConfig { IsUnknownVersion = true }; }
        }

        WikiPromptConfig? cfg = null;
        try { cfg = JsonSerializer.Deserialize<WikiPromptConfig>(json!, JsonOpts); }
        catch
        {
            RecoverBrokenConfigFile(path);
            return new WikiPromptConfig();
        }

        if (cfg == null) return new WikiPromptConfig();
        if (cfg.Version != 1) { cfg.IsUnknownVersion = true; return cfg; }
        return cfg;
    }

    public async Task SavePromptsAtomicAsync(string wikiRoot, WikiPromptConfig config)
        => await WriteJsonAtomicAsync(GetPromptsConfigPath(wikiRoot), config);

    // ---- AGENTS.md カテゴリブロック ----

    private const string AgentsBegin = "<!-- CURIA:CATEGORIES:BEGIN -->";
    private const string AgentsEnd   = "<!-- CURIA:CATEGORIES:END -->";

    public async Task UpdateAgentsMdCategoryBlockAsync(string wikiRoot, IReadOnlyList<string> categories)
    {
        var agentsPath = Path.Combine(wikiRoot, "AGENTS.md");
        if (!File.Exists(agentsPath)) return;

        var content = await File.ReadAllTextAsync(agentsPath, Encoding.UTF8);
        var beginIdx = content.IndexOf(AgentsBegin, StringComparison.Ordinal);
        var endIdx   = content.IndexOf(AgentsEnd,   StringComparison.Ordinal);

        var newBlock = BuildCategoryBlock(categories);

        if (beginIdx < 0 && endIdx < 0)
        {
            // 管理ブロックなし → 末尾に追記
            content = content.TrimEnd() + "\n\n" + AgentsBegin + "\n" + newBlock + AgentsEnd + "\n";
        }
        else if (beginIdx >= 0 && endIdx > beginIdx)
        {
            // 管理ブロック置換
            var before = content[..(beginIdx + AgentsBegin.Length)];
            var after  = content[endIdx..];
            content = before + "\n" + newBlock + after;
        }
        else
        {
            // 不正構造: 更新せず
            return;
        }

        await WriteFileAtomicAsync(agentsPath, content);
    }

    public (bool IsValid, string? Issue) CheckAgentsMdCategoryBlock(string wikiRoot, IReadOnlyList<string> categories)
    {
        var agentsPath = Path.Combine(wikiRoot, "AGENTS.md");
        if (!File.Exists(agentsPath)) return (true, null);

        string content;
        try { content = File.ReadAllText(agentsPath, Encoding.UTF8); }
        catch { return (true, null); }

        var beginCount = CountOccurrences(content, AgentsBegin);
        var endCount   = CountOccurrences(content, AgentsEnd);

        if (beginCount > 1 || endCount > 1)
            return (false, "AGENTS.md has multiple CURIA:CATEGORIES blocks. Manual repair required.");
        if (beginCount != endCount)
            return (false, "AGENTS.md has mismatched CURIA:CATEGORIES BEGIN/END markers. Manual repair required.");
        if (beginCount == 0) return (true, null); // ブロックなし: 乖離判定対象外

        var beginIdx = content.IndexOf(AgentsBegin, StringComparison.Ordinal);
        var endIdx   = content.IndexOf(AgentsEnd,   StringComparison.Ordinal);
        if (endIdx < beginIdx)
            return (false, "AGENTS.md has CURIA:CATEGORIES END before BEGIN. Manual repair required.");

        var blockContent = content[(beginIdx + AgentsBegin.Length)..endIdx];
        var blockCats = blockContent.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("- `pages/") && l.EndsWith("/`"))
            .Select(l => { var s = l[("- `pages/".Length)..]; return s[..^2].ToLowerInvariant(); })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var configCats = categories.Select(c => c.ToLowerInvariant()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = configCats.Except(blockCats).ToList();
        var extra   = blockCats.Except(configCats).ToList();

        if (missing.Count > 0 || extra.Count > 0)
        {
            var issues = new List<string>();
            if (missing.Count > 0) issues.Add($"Missing: {string.Join(", ", missing)}");
            if (extra.Count > 0)   issues.Add($"Extra: {string.Join(", ", extra)}");
            return (false, $"AGENTS.md category block diverged from config. {string.Join("; ", issues)}");
        }

        return (true, null);
    }

    private static string BuildCategoryBlock(IReadOnlyList<string> categories)
    {
        var sb = new StringBuilder();
        foreach (var cat in categories)
            sb.AppendLine($"- `pages/{cat.ToLowerInvariant()}/`");
        return sb.ToString();
    }

    private static int CountOccurrences(string text, string pattern)
    {
        int count = 0, idx = 0;
        while ((idx = text.IndexOf(pattern, idx, StringComparison.Ordinal)) >= 0) { count++; idx += pattern.Length; }
        return count;
    }

    // ---- 保存トランザクション ----

    public async Task<WikiTransaction> BeginTransactionAsync(
        string wikiRoot,
        IEnumerable<(string relPath, bool isCreate)> targets)
    {
        var txn = new WikiTransaction { Phase = WikiTxnPhase.Prepared };
        var pagesDir = GetPagesDir(wikiRoot);

        foreach (var (relPath, isCreate) in targets)
        {
            var normalizedRel = NormalizeRelativePath(relPath);
            string targetPath;
            if (relPath is "index.md" or "log.md")
                targetPath = Path.Combine(wikiRoot, normalizedRel);
            else
                targetPath = Path.Combine(wikiRoot, normalizedRel.Replace('/', Path.DirectorySeparatorChar));

            var dir = Path.GetDirectoryName(targetPath)!;
            var fileName = Path.GetFileName(targetPath);
            var tempPath   = Path.Combine(dir, $"{fileName}.tmp-{Guid.NewGuid():N}");
            var backupPath = Path.Combine(dir, $"{fileName}.bak-{Guid.NewGuid():N}");

            var entry = new WikiTxnEntry
            {
                EntryType  = isCreate ? WikiTxnEntryType.Create : WikiTxnEntryType.Update,
                TargetPath = targetPath,
                TempPath   = tempPath,
                BackupPath = backupPath,
                State      = WikiTxnEntryState.Prepared
            };
            txn.Entries.Add(entry);
        }

        // 重複チェック
        var fullPaths = txn.Entries.Select(e => Path.GetFullPath(e.TargetPath)).ToList();
        var dupCheck  = fullPaths.GroupBy(p => p, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).ToList();
        if (dupCheck.Count > 0)
            throw new InvalidOperationException($"Duplicate target paths in transaction: {string.Join(", ", dupCheck.Select(g => g.Key))}");

        // ジャーナル書き込み
        await WriteJsonAtomicAsync(GetTxnPath(wikiRoot), txn);
        return txn;
    }

    public async Task CommitTransactionAsync(
        string wikiRoot, WikiTransaction txn,
        Func<WikiTxnEntry, Task<string>> contentProvider)
    {
        txn.Phase = WikiTxnPhase.Committing;
        await WriteJsonAtomicAsync(GetTxnPath(wikiRoot), txn);

        foreach (var entry in txn.Entries)
        {
            // 既存ファイルをバックアップ (Update のみ)
            if (entry.EntryType == WikiTxnEntryType.Update && File.Exists(entry.TargetPath))
            {
                File.Copy(entry.TargetPath, entry.BackupPath, overwrite: true);
            }

            // コンテンツを一時ファイルへ書き込み
            var content = await contentProvider(entry);
            await File.WriteAllTextAsync(entry.TempPath, content, Encoding.UTF8);
            entry.State = WikiTxnEntryState.TempWritten;
            await WriteJsonAtomicAsync(GetTxnPath(wikiRoot), txn);

            // 本番へ適用
            Directory.CreateDirectory(Path.GetDirectoryName(entry.TargetPath)!);
            if (entry.EntryType == WikiTxnEntryType.Update && File.Exists(entry.TargetPath))
                File.Replace(entry.TempPath, entry.TargetPath, null);
            else
                File.Move(entry.TempPath, entry.TargetPath, overwrite: false);

            entry.State = WikiTxnEntryState.Replaced;
            await WriteJsonAtomicAsync(GetTxnPath(wikiRoot), txn);
        }

        txn.Phase = WikiTxnPhase.Committed;
        await WriteJsonAtomicAsync(GetTxnPath(wikiRoot), txn);

        // クリーンアップ
        CleanupTransaction(txn);
        try { File.Delete(GetTxnPath(wikiRoot)); } catch { /* 次回起動で再試行 */ }
    }

    public async Task RollbackTransactionAsync(string wikiRoot, WikiTransaction txn)
    {
        txn.Phase = WikiTxnPhase.Rollbacking;
        try { await WriteJsonAtomicAsync(GetTxnPath(wikiRoot), txn); } catch { /* ignore */ }

        foreach (var entry in txn.Entries)
        {
            try
            {
                if (entry.State == WikiTxnEntryState.Replaced)
                {
                    if (entry.EntryType == WikiTxnEntryType.Update && File.Exists(entry.BackupPath))
                        File.Replace(entry.BackupPath, entry.TargetPath, null);
                    else if (entry.EntryType == WikiTxnEntryType.Create && File.Exists(entry.TargetPath))
                    {
                        // 外部変更確認: サイズが一致しない場合は隔離
                        File.Delete(entry.TargetPath);
                    }
                }
                else if (entry.State == WikiTxnEntryState.TempWritten)
                {
                    try { File.Delete(entry.TempPath); } catch { }
                    if (entry.EntryType == WikiTxnEntryType.Update && File.Exists(entry.BackupPath))
                        File.Replace(entry.BackupPath, entry.TargetPath, null);
                }
                entry.State = WikiTxnEntryState.RolledBack;
            }
            catch { /* best effort */ }
        }

        txn.Phase = WikiTxnPhase.RolledBack;
        try { await WriteJsonAtomicAsync(GetTxnPath(wikiRoot), txn); } catch { }
        CleanupTransaction(txn);
        try { File.Delete(GetTxnPath(wikiRoot)); } catch { }
    }

    public async Task RecoverPendingTransactionAsync(string wikiRoot)
    {
        var txnPath = GetTxnPath(wikiRoot);
        if (!File.Exists(txnPath)) return;

        WikiTransaction? txn = null;
        try { txn = JsonSerializer.Deserialize<WikiTransaction>(File.ReadAllText(txnPath, Encoding.UTF8), JsonOpts); }
        catch { try { File.Delete(txnPath); } catch { } return; }
        if (txn == null) { try { File.Delete(txnPath); } catch { } return; }

        switch (txn.Phase)
        {
            case WikiTxnPhase.Committed:
            case WikiTxnPhase.RolledBack:
                CleanupTransaction(txn);
                try { File.Delete(txnPath); } catch { }
                return;

            case WikiTxnPhase.Prepared:
                // temp_written/replaced が1件でもあれば再ロールバック
                if (txn.Entries.Any(e => e.State >= WikiTxnEntryState.TempWritten))
                    await RollbackTransactionAsync(wikiRoot, txn);
                else
                    try { File.Delete(txnPath); } catch { }
                return;

            case WikiTxnPhase.Committing:
                // 全件 replaced なら後片付け、そうでなければロールバック
                if (txn.Entries.All(e => e.State == WikiTxnEntryState.Replaced))
                {
                    txn.Phase = WikiTxnPhase.Committed;
                    CleanupTransaction(txn);
                    try { File.Delete(txnPath); } catch { }
                }
                else
                    await RollbackTransactionAsync(wikiRoot, txn);
                return;

            case WikiTxnPhase.Rollbacking:
                await RollbackTransactionAsync(wikiRoot, txn);
                return;
        }
    }

    private static void CleanupTransaction(WikiTransaction txn)
    {
        foreach (var entry in txn.Entries)
        {
            try { if (File.Exists(entry.TempPath))   File.Delete(entry.TempPath); } catch { }
            try { if (File.Exists(entry.BackupPath))  File.Delete(entry.BackupPath); } catch { }
        }
    }

    // ---- ドメイン排他ロック ----

    public static string GetDomainMutexName(string wikiRoot)
    {
        var normalized = Path.GetFullPath(wikiRoot).ToUpperInvariant();
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(normalized));
        var hex  = BitConverter.ToString(hash).Replace("-", "")[..16];
        return $"Global\\Curia_Wiki_{hex}";
    }

    /// <summary>
    /// ドメインロックを取得する。
    /// 最大30秒 x 2回試行。タイムアウトで WikiDomainLockException。
    /// </summary>
    public static async Task<IDisposable> AcquireDomainLockAsync(
        string wikiRoot,
        CancellationToken cancellationToken = default)
    {
        var mutexName = GetDomainMutexName(wikiRoot);
        Mutex? mutex = null;

        for (int attempt = 0; attempt < 2; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { mutex = new Mutex(false, mutexName); }
            catch { mutex = null; break; }

            bool acquired = false;
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (mutex.WaitOne(500))
                    {
                        acquired = true;
                        break;
                    }
                }
                catch (AbandonedMutexException)
                {
                    // 前回異常終了: ロック取得成功として扱う
                    acquired = true;
                    break;
                }
                await Task.Delay(100, cancellationToken);
            }

            if (acquired)
                return new MutexReleaser(mutex);

            mutex.Dispose();
            mutex = null;
            if (attempt < 1)
                await Task.Delay(1000, cancellationToken);
        }

        throw new WikiDomainLockException(wikiRoot);
    }

    private sealed class MutexReleaser : IDisposable
    {
        private readonly Mutex _mutex;
        private bool _disposed;
        public MutexReleaser(Mutex mutex) => _mutex = mutex;
        public void Dispose() { if (!_disposed) { _mutex.ReleaseMutex(); _mutex.Dispose(); _disposed = true; } }
    }

    // ---- pages/ 未定義カテゴリスキャン ----

    /// <summary>
    /// pages/ 配下の全ディレクトリを取得し、定義済み + 未定義に分類して返す。
    /// 表示順: sources 先頭 → 設定ファイル順 → 未定義（名前昇順）
    /// </summary>
    public (IReadOnlyList<string> DefinedCategories, IReadOnlyList<string> UndefinedCategories)
        GetCategoryDisplayList(string wikiRoot)
    {
        var cfg = LoadCategories(wikiRoot);
        var defined = cfg.Categories.Select(c => c.ToLowerInvariant()).ToList();

        // sources を先頭に
        defined.Remove("sources");
        defined.Insert(0, "sources");
        // 重複排除
        defined = defined.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var pagesDir = GetPagesDir(wikiRoot);
        var undefined = new List<string>();
        if (Directory.Exists(pagesDir))
        {
            var existing = Directory.EnumerateDirectories(pagesDir, "*", SearchOption.TopDirectoryOnly)
                .Select(d => Path.GetFileName(d)!.ToLowerInvariant())
                .Where(n => !string.IsNullOrEmpty(n))
                .OrderBy(n => n)
                .ToList();
            undefined = existing.Where(n => !defined.Any(d => d.Equals(n, StringComparison.OrdinalIgnoreCase))).ToList();
        }

        return (defined, undefined);
    }
}
