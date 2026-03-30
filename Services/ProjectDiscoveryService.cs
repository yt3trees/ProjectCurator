using System.IO;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ProjectCurator.Models;

namespace ProjectCurator.Services;

public record BoxOnlyProjectCandidate(
    string Name,
    string Tier,
    string Category,
    string BoxPath,
    List<string> ExternalSharedPaths)
{
    public string DisplayName => Category == "domain"
        ? (Tier == "mini" ? $"{Name} [Domain][Mini]" : $"{Name} [Domain]")
        : (Tier == "mini" ? $"{Name} [Mini]" : Name);
}

public class ProjectDiscoveryService
{
    private readonly ConfigService _configService;

    private List<ProjectInfo>? _cache;
    private DateTime _cacheTime = DateTime.MinValue;
    private const int CacheTtlSeconds = 300; // 5 minutes

    public ProjectDiscoveryService(ConfigService configService)
    {
        _configService = configService;
    }

    public async Task<List<ProjectInfo>> GetProjectInfoListAsync(bool force = false, CancellationToken ct = default)
    {
        if (!force && _cache != null && (DateTime.Now - _cacheTime).TotalSeconds < CacheTtlSeconds)
            return _cache;

        var result = await Task.Run(() => ScanProjects(), ct);
        _cache = result;
        _cacheTime = DateTime.Now;
        return result;
    }

    public List<ProjectInfo> GetProjectInfoList(bool force = false)
    {
        if (!force && _cache != null && (DateTime.Now - _cacheTime).TotalSeconds < CacheTtlSeconds)
            return _cache;

        _cache = ScanProjects();
        _cacheTime = DateTime.Now;
        return _cache;
    }

    public void InvalidateCache() => _cache = null;

    // =====================================================================
    // Phase 5: PowerShell スクリプトのネイティブ移植
    // =====================================================================

    /// <summary>
    /// 指定されたプロジェクトの整合性チェックを行う (check_project.ps1 移植)
    /// </summary>
    public async Task<ProjectCheckResult> CheckProjectAsync(ProjectInfo project)
    {
        return await Task.Run(() =>
        {
            var result = new ProjectCheckResult { ProjectName = project.Name, ProjectPath = project.Path };
            CheckDir(result, "Root Directory", project.Path, true);
            CheckDir(result, "AI Context Directory (_ai-context)", project.AiContextPath, true);
            CheckJunction(result, "shared", Path.Combine(project.Path, "shared"), true);
            CheckJunction(result, "obsidian_notes", Path.Combine(project.AiContextPath, "obsidian_notes"), false);
            CheckJunction(result, "context (Junction to Obsidian)", project.AiContextContentPath, true);
            CheckFile(result, "current_focus.md", Path.Combine(project.AiContextContentPath, "current_focus.md"), true);
            CheckFile(result, "project_summary.md", Path.Combine(project.AiContextContentPath, "project_summary.md"), true);
            CheckFile(result, "file_map.md", Path.Combine(project.AiContextContentPath, "file_map.md"), false);
            CheckFile(result, "CLAUDE.md", Path.Combine(project.Path, "CLAUDE.md"), false);
            CheckFile(result, "AGENTS.md", Path.Combine(project.Path, "AGENTS.md"), false);
            return result;
        });
    }

    /// <summary>
    /// 指定されたプロジェクトをアーカイブフォルダへ移動する (archive_project.ps1 移植)
    /// </summary>
    public async Task<ProjectArchiveResult> ArchiveProjectAsync(ProjectInfo project, bool dryRun)
    {
        return await Task.Run(() =>
        {
            var result = new ProjectArchiveResult { Success = true };
            var paths = _configService.LoadSettings();
            var localRoot = Environment.ExpandEnvironmentVariables(paths.LocalProjectsRoot);
            var syncRoot = Environment.ExpandEnvironmentVariables(paths.CloudSyncRoot);
            var obsidianRoot = Environment.ExpandEnvironmentVariables(paths.ObsidianVaultRoot);

            string categoryPrefix = project.Category == "domain" ? "_domains" : "";
            string projectSubPath = project.Tier == "mini" ? Path.Combine(categoryPrefix, "_mini", project.Name) : Path.Combine(categoryPrefix, project.Name);
            string archiveSubPath = project.Tier == "mini" ? Path.Combine("_archive", categoryPrefix, "_mini", project.Name) : Path.Combine("_archive", categoryPrefix, project.Name);

            string docRoot = Path.Combine(localRoot, projectSubPath);
            string boxShared = Path.Combine(syncRoot, projectSubPath);
            string obsidianProject = project.Name == "_INHOUSE" ? Path.Combine(obsidianRoot, "_INHOUSE") : Path.Combine(obsidianRoot, "Projects", projectSubPath);

            string localArchive = Path.Combine(localRoot, archiveSubPath);
            string boxArchive = Path.Combine(syncRoot, archiveSubPath);
            string obsidianArchive = project.Name == "_INHOUSE" ? Path.Combine(obsidianRoot, "_archive", "_INHOUSE") : Path.Combine(obsidianRoot, "Projects", archiveSubPath);

            if (Directory.Exists(localArchive) || Directory.Exists(boxArchive) || Directory.Exists(obsidianArchive))
            {
                result.Success = false;
                result.Message = "Archive destination already exists.";
                return result;
            }

            if (!dryRun)
            {
                RemoveJunctionInternal(Path.Combine(docRoot, "shared"));
                RemoveJunctionInternal(Path.Combine(project.AiContextPath, "obsidian_notes"));
                RemoveJunctionInternal(project.AiContextContentPath);
                
                MoveFolderInternal(boxShared, boxArchive, result.Logs);
                MoveFolderInternal(obsidianProject, obsidianArchive, result.Logs);
                MoveFolderInternal(docRoot, localArchive, result.Logs);
                result.Logs.Add("Archive complete.");
            }
            else
            {
                result.Logs.Add("[DRY RUN] Would move folders to _archive/");
            }
            return result;
        });
    }

    /// <summary>
    /// プロジェクトの Tier を変換する (convert_tier.ps1 移植)
    /// </summary>
    public async Task<ProjectConvertResult> ConvertProjectTierAsync(ProjectInfo project, string toTier, bool dryRun)
    {
        return await Task.Run(() =>
        {
            var result = new ProjectConvertResult { Success = true };
            var paths = _configService.LoadSettings();
            var localRoot = Environment.ExpandEnvironmentVariables(paths.LocalProjectsRoot);
            var syncRoot = Environment.ExpandEnvironmentVariables(paths.CloudSyncRoot);
            var obsidianRoot = Environment.ExpandEnvironmentVariables(paths.ObsidianVaultRoot);

            string categoryPrefix = project.Category == "domain" ? "_domains" : "";
            string srcSubPath = project.Tier == "mini" ? Path.Combine(categoryPrefix, "_mini", project.Name) : Path.Combine(categoryPrefix, project.Name);
            string dstSubPath = toTier == "mini" ? Path.Combine(categoryPrefix, "_mini", project.Name) : Path.Combine(categoryPrefix, project.Name);

            string srcLocal = Path.Combine(localRoot, srcSubPath);
            string dstLocal = Path.Combine(localRoot, dstSubPath);
            string srcBox = Path.Combine(syncRoot, srcSubPath);
            string dstBox = Path.Combine(syncRoot, dstSubPath);
            string srcObsidian = project.Name == "_INHOUSE" ? Path.Combine(obsidianRoot, "_INHOUSE") : Path.Combine(obsidianRoot, "Projects", srcSubPath);
            string dstObsidian = project.Name == "_INHOUSE" ? Path.Combine(obsidianRoot, "_INHOUSE") : Path.Combine(obsidianRoot, "Projects", dstSubPath);

            if (Directory.Exists(dstLocal)) { result.Success = false; result.Message = "Destination exists."; return result; }

            if (!dryRun)
            {
                RemoveJunctionInternal(Path.Combine(srcLocal, "shared"));
                RemoveJunctionInternal(Path.Combine(srcLocal, "_ai-context", "obsidian_notes"));
                RemoveJunctionInternal(Path.Combine(srcLocal, "_ai-context", "context"));
                foreach (var cli in new[] { ".claude", ".codex", ".gemini" })
                    RemoveJunctionInternal(Path.Combine(srcLocal, cli));

                MoveFolderInternal(srcBox, dstBox, result.Logs);
                MoveFolderInternal(srcObsidian, dstObsidian, result.Logs);
                MoveFolderInternal(srcLocal, dstLocal, result.Logs);

                if (toTier == "full") Directory.CreateDirectory(Path.Combine(dstLocal, "_ai-workspace"));

                CreateJunctionInternal(Path.Combine(dstLocal, "shared"), dstBox);
                CreateJunctionInternal(Path.Combine(dstLocal, "_ai-context", "obsidian_notes"), dstObsidian);
                CreateJunctionInternal(Path.Combine(dstLocal, "_ai-context", "context"), Path.Combine(dstObsidian, "ai-context"));
                foreach (var cli in new[] { ".claude", ".codex", ".gemini" })
                    CreateJunctionInternal(Path.Combine(dstLocal, cli), Path.Combine(dstBox, cli));
                
                result.Logs.Add($"Converted to {toTier}.");
            }
            return result;
        });
    }

    /// <summary>
    /// 新規プロジェクトのセットアップ (setup_project.ps1 移植)
    /// </summary>
    public async Task<ProjectSetupResult> SetupProjectAsync(string projectName, string tier, string category, string[] externalSharedPaths)
    {
        return await Task.Run(() =>
        {
            var result = new ProjectSetupResult { Success = true };
            var paths = _configService.LoadSettings();
            var localRoot = Environment.ExpandEnvironmentVariables(paths.LocalProjectsRoot);
            var syncRoot = Environment.ExpandEnvironmentVariables(paths.CloudSyncRoot);
            var obsidianRoot = Environment.ExpandEnvironmentVariables(paths.ObsidianVaultRoot);

            string categoryPrefix = category == "domain" ? "_domains" : "";
            string subPath = tier == "mini" ? Path.Combine(categoryPrefix, "_mini", projectName) : Path.Combine(categoryPrefix, projectName);

            string docRoot = Path.Combine(localRoot, subPath);
            string boxShared = Path.Combine(syncRoot, subPath);
            string obsidianProject = projectName == "_INHOUSE" ? Path.Combine(obsidianRoot, "_INHOUSE") : Path.Combine(obsidianRoot, "Projects", subPath);

            Directory.CreateDirectory(docRoot);
            Directory.CreateDirectory(Path.Combine(docRoot, "_ai-context"));
            if (tier == "full") Directory.CreateDirectory(Path.Combine(docRoot, "_ai-workspace"));
            Directory.CreateDirectory(Path.Combine(docRoot, "development", "source"));

            Directory.CreateDirectory(boxShared);
            Directory.CreateDirectory(Path.Combine(boxShared, "docs"));
            Directory.CreateDirectory(Path.Combine(boxShared, "_work"));

            Directory.CreateDirectory(obsidianProject);
            var obsidianAiContext = Path.Combine(obsidianProject, "ai-context");
            Directory.CreateDirectory(obsidianAiContext);
            foreach (var folder in new[] { "troubleshooting", "daily", "meetings", "notes" })
            {
                Directory.CreateDirectory(Path.Combine(obsidianProject, folder));
            }

            EnsureAiContextCoreFiles(obsidianAiContext, projectName, tier, result.Logs);

            EnsureJunctionWithLog(Path.Combine(docRoot, "shared"), boxShared, "shared", result.Logs);
            SetupExternalSharedPaths(docRoot, boxShared, externalSharedPaths, result.Logs);
            EnsureJunctionWithLog(Path.Combine(docRoot, "_ai-context", "obsidian_notes"), obsidianProject, "_ai-context/obsidian_notes", result.Logs);
            EnsureJunctionWithLog(Path.Combine(docRoot, "_ai-context", "context"), Path.Combine(obsidianProject, "ai-context"), "_ai-context/context", result.Logs);

            // AGENTS.md / CLAUDE.md
            CreateAiInstructionFiles(projectName, docRoot, boxShared, result.Logs);

            // .git/forCodex (Codex CLI AGENTS.md discovery marker)
            CreateForCodexMarker(boxShared, result.Logs);

            result.Logs.Add("Setup complete.");
            return result;
        });
    }

    // =====================================================================
    // Private Helpers
    // =====================================================================

    private static void SetupExternalSharedPaths(string docRoot, string boxShared, string[] newPaths, List<string> logs)
    {
        var configFile = Path.Combine(boxShared, "external_shared_paths");
        var oldConfigFile = Path.Combine(boxShared, ".external_shared_paths");
        var externalSharedDir = Path.Combine(docRoot, "external_shared");

        // .external_shared_paths → external_shared_paths マイグレーション
        if (File.Exists(oldConfigFile))
        {
            if (!File.Exists(configFile))
            {
                File.Move(oldConfigFile, configFile);
                logs.Add("  Migrated: .external_shared_paths -> external_shared_paths");
            }
            else
            {
                File.Delete(oldConfigFile);
                logs.Add("  Removed old .external_shared_paths (new file already exists)");
            }
        }

        // 既存設定を読み込む
        var existingPaths = File.Exists(configFile)
            ? File.ReadAllLines(configFile).Where(l => !string.IsNullOrWhiteSpace(l)).ToList()
            : [];

        // 新規パスを正規化して既存パスとマージ
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var normalizedNew = newPaths
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim().Replace(userProfile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase))
            .ToList();

        List<string> pathsToProcess;
        if (normalizedNew.Count > 0)
        {
            pathsToProcess = existingPaths.Concat(normalizedNew).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            File.WriteAllLines(configFile, pathsToProcess, new System.Text.UTF8Encoding(false));
            logs.Add($"  Saved External Shared Paths to: external_shared_paths");
            if (existingPaths.Count > 0)
                logs.Add($"    (Merged with {existingPaths.Count} existing path(s))");
        }
        else
        {
            pathsToProcess = existingPaths;
        }

        if (pathsToProcess.Count == 0) return;

        // external_shared/ ディレクトリ作成
        if (!Directory.Exists(externalSharedDir))
        {
            Directory.CreateDirectory(externalSharedDir);
            logs.Add("  Created: external_shared/ (Directory)");
        }

        // 各パスのジャンクション作成
        foreach (var savedPath in pathsToProcess)
        {
            var expandedPath = Environment.ExpandEnvironmentVariables(savedPath.Trim());
            var folderName = Path.GetFileName(expandedPath.TrimEnd(Path.DirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(folderName))
            {
                logs.Add($"  [WARN] Could not determine folder name for: {expandedPath}");
                continue;
            }

            var linkPath = Path.Combine(externalSharedDir, folderName);
            if (Directory.Exists(linkPath))
            {
                var info = new DirectoryInfo(linkPath);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                    logs.Add($"  OK: external_shared/{folderName}/ -> {expandedPath}");
                else
                    logs.Add($"  [WARN] external_shared/{folderName}/ exists but is not a junction");
            }
            else if (Directory.Exists(expandedPath))
            {
                var created = TryCreateJunction(linkPath, expandedPath);
                if (created.Success)
                    logs.Add($"  Created: external_shared/{folderName}/ -> {expandedPath}");
                else
                    logs.Add($"  [WARN] Failed to create external_shared/{folderName}/ ({created.Reason})");
            }
            else
            {
                logs.Add($"  [WARN] External Shared Folder not found: {expandedPath}");
            }
        }
    }

    private void CreateAiInstructionFiles(string projectName, string docRoot, string boxShared, List<string> logs)
    {
        var boxAgents = Path.Combine(boxShared, "AGENTS.md");
        var boxClaude = Path.Combine(boxShared, "CLAUDE.md");
        var localAgents = Path.Combine(docRoot, "AGENTS.md");
        var localClaude = Path.Combine(docRoot, "CLAUDE.md");

        // BOX: AGENTS.md (アプリ内テンプレートから生成、既存は上書きしない)
        if (!File.Exists(boxAgents))
        {
            var content = ContextCompressionLayerService.BuildAgentsTemplate(projectName);
            File.WriteAllText(boxAgents, content, new System.Text.UTF8Encoding(false));
            logs.Add($"  Created: {boxAgents}");
        }
        else
        {
            logs.Add($"  Exists: {boxAgents}");
        }

        // BOX: CLAUDE.md (@AGENTS.md reference)
        if (!File.Exists(boxClaude))
        {
            File.WriteAllText(boxClaude, "@AGENTS.md", new System.Text.UTF8Encoding(false));
            logs.Add($"  Created: {boxClaude} (@AGENTS.md reference)");
        }
        else
        {
            logs.Add($"  Exists: {boxClaude}");
        }

        // Local: AGENTS.md (BOX からコピー)
        File.Copy(boxAgents, localAgents, overwrite: true);
        logs.Add("  Copied: AGENTS.md -> Local Project Root");

        // Local: CLAUDE.md (@AGENTS.md reference)
        File.WriteAllText(localClaude, "@AGENTS.md", new System.Text.UTF8Encoding(false));
        logs.Add("  Created: CLAUDE.md -> Local Project Root (@AGENTS.md reference)");
    }

    private static void CreateForCodexMarker(string boxShared, List<string> logs)
    {
        var gitDir = Path.Combine(boxShared, ".git");
        var forCodexFile = Path.Combine(gitDir, "forCodex");
        if (!Directory.Exists(gitDir))
        {
            Directory.CreateDirectory(gitDir);
            logs.Add("  Created: .git/");
        }
        if (!File.Exists(forCodexFile))
        {
            File.WriteAllText(forCodexFile,
                "This marker lets Codex CLI treat this directory as a repo root so that AGENTS.md is discoverable from _work/.",
                new System.Text.UTF8Encoding(false));
            logs.Add("  Created: .git/forCodex");
        }
    }

    private static void EnsureAiContextCoreFiles(
        string obsidianAiContext,
        string projectName,
        string tier,
        List<string> logs)
    {
        WriteIfMissing(
            Path.Combine(obsidianAiContext, "project_summary.md"),
            ContextCompressionLayerService.ProjectSummaryTemplate,
            logs);

        WriteIfMissing(
            Path.Combine(obsidianAiContext, "current_focus.md"),
            ContextCompressionLayerService.CurrentFocusTemplate,
            logs);

        WriteIfMissing(
            Path.Combine(obsidianAiContext, "tensions.md"),
            """
            # Tensions

            ## Open technical questions

            - 

            ## Unresolved trade-offs

            - 

            ## Risks and concerns

            - 

            ---
            Last Update: YYYY-MM-DD
            """,
            logs);

        WriteIfMissing(
            Path.Combine(obsidianAiContext, "file_map.md"),
            BuildInitialFileMap(projectName, tier),
            logs);
    }

    private static void WriteIfMissing(string path, string content, List<string> logs)
    {
        if (File.Exists(path))
        {
            logs.Add($"  Exists: {path}");
            return;
        }

        File.WriteAllText(path, content, new UTF8Encoding(false));
        logs.Add($"  Created: {path}");
    }

    private static string BuildInitialFileMap(string projectName, string tier)
        => $"""
        # File Map

        ## Project

        - Name: {projectName}
        - Tier: {tier}

        ## Core context files

        | File | Purpose |
        |------|---------|
        | `project_summary.md` | Project overview and goals |
        | `current_focus.md` | Current focus and near-term tasks |
        | `tensions.md` | Open issues, trade-offs, and risks |
        | `file_map.md` | This map |

        ## Notes

        - Add project-specific entries as needed.
        """;

    private void RemoveJunctionInternal(string path)
    {
        if (!Directory.Exists(path)) return;
        var info = new DirectoryInfo(path);
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0) info.Delete();
    }

    private void CreateJunctionInternal(string linkPath, string targetPath)
    {
        if (!Directory.Exists(targetPath) || Directory.Exists(linkPath)) return;
        _ = TryCreateJunction(linkPath, targetPath);
    }

    private static void EnsureJunctionWithLog(string linkPath, string targetPath, string label, List<string> logs)
    {
        if (!Directory.Exists(targetPath))
        {
            logs.Add($"  [WARN] {label} junction target is missing: {targetPath}");
            return;
        }

        if (Directory.Exists(linkPath))
        {
            var existing = new DirectoryInfo(linkPath);
            if ((existing.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                logs.Add($"  [SKIP] {label} junction already exists.");
            }
            else
            {
                logs.Add($"  [WARN] {label} exists but is not a junction: {linkPath}");
            }
            return;
        }

        var creation = TryCreateJunction(linkPath, targetPath);
        if (creation.Success)
        {
            logs.Add($"  Created junction: {label} -> {targetPath}");
            return;
        }

        var reason = string.IsNullOrWhiteSpace(creation.Reason) ? "unknown error" : creation.Reason;
        logs.Add($"  [ERROR] Failed to create junction: {label} -> {targetPath} ({reason})");
    }

    private static (bool Success, string Reason) TryCreateJunction(string linkPath, string targetPath)
    {
        static string EscapeSingleQuotes(string value) => value.Replace("'", "''");

        var psCommand = $"New-Item -ItemType Junction -Path '{EscapeSingleQuotes(linkPath)}' -Target '{EscapeSingleQuotes(targetPath)}' -Force | Out-Null";
        var encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(psCommand));

        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NonInteractive -NoProfile -EncodedCommand {encoded}",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        });

        if (process == null)
        {
            return (false, "failed to start PowerShell process");
        }

        process.WaitForExit();
        var stdout = process.StandardOutput.ReadToEnd().Trim();
        var stderr = process.StandardError.ReadToEnd().Trim();
        var created = Directory.Exists(linkPath) && (new DirectoryInfo(linkPath).Attributes & FileAttributes.ReparsePoint) != 0;

        if (process.ExitCode == 0 && created)
        {
            return (true, "");
        }

        var reason = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
        return (false, reason);
    }

    private void MoveFolderInternal(string src, string dst, List<string> logs)
    {
        if (!Directory.Exists(src)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
        Directory.Move(src, dst);
        logs.Add($"Moved: {Path.GetFileName(src)}");
    }

    private static void CheckDir(ProjectCheckResult result, string name, string path, bool mandatory)
    {
        bool exists = Directory.Exists(path);
        result.Items.Add(new CheckItem { Name = name, Status = exists ? "OK" : (mandatory ? "Error" : "Warning"), Message = exists ? "Exists" : "Missing" });
    }

    private static void CheckFile(ProjectCheckResult result, string name, string path, bool mandatory)
    {
        bool exists = File.Exists(path);
        result.Items.Add(new CheckItem { Name = name, Status = exists ? "OK" : (mandatory ? "Error" : "Warning"), Message = exists ? "Exists" : "Missing" });
    }

    private static void CheckJunction(ProjectCheckResult result, string name, string path, bool mandatory)
    {
        var status = GetJunctionStatus(path);
        result.Items.Add(new CheckItem { Name = name, Status = status == "OK" ? "OK" : (mandatory ? "Error" : "Warning"), Message = status });
    }

    private static string GetJunctionStatus(string path)
    {
        var info = new DirectoryInfo(path);
        if (!info.Exists) return "Missing";
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            var target = info.LinkTarget;
            return (string.IsNullOrEmpty(target) || !Directory.Exists(target)) ? "Broken" : "OK";
        }
        return "Not a Junction";
    }

    public Task<List<BoxOnlyProjectCandidate>> ScanBoxOnlyProjectsAsync(CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            var settings  = _configService.LoadSettings();
            var localRoot = settings.LocalProjectsRoot;
            var syncRoot   = settings.CloudSyncRoot;

            if (!Directory.Exists(syncRoot)) return [];

            var results = new List<BoxOnlyProjectCandidate>();

            // tier=full, category=project (root 直下, ScanProjects と同ルール)
            foreach (var dir in SafeEnumerateDirectories(syncRoot))
            {
                var name = Path.GetFileName(dir);
                if (name != "_INHOUSE" && (name.StartsWith('_') || name.StartsWith('.'))) continue;
                if (!Directory.Exists(Path.Combine(localRoot, name)))
                    results.Add(MakeBoxCandidate(name, "full", "project", dir));
            }

            // tier=mini, category=project
            var boxMini = Path.Combine(syncRoot, "_mini");
            if (Directory.Exists(boxMini))
            {
                foreach (var dir in SafeEnumerateDirectories(boxMini))
                {
                    var name = Path.GetFileName(dir);
                    if (name.StartsWith('_') || name.StartsWith('.')) continue;
                    if (!Directory.Exists(Path.Combine(localRoot, "_mini", name)))
                        results.Add(MakeBoxCandidate(name, "mini", "project", dir));
                }
            }

            // tier=full, category=domain
            var boxDomains = Path.Combine(syncRoot, "_domains");
            if (Directory.Exists(boxDomains))
            {
                foreach (var dir in SafeEnumerateDirectories(boxDomains))
                {
                    var name = Path.GetFileName(dir);
                    if (name.StartsWith('_') || name.StartsWith('.')) continue;
                    if (!Directory.Exists(Path.Combine(localRoot, "_domains", name)))
                        results.Add(MakeBoxCandidate(name, "full", "domain", dir));
                }
            }

            return results
                .OrderBy(c => c.Name == "_INHOUSE" ? 0 : c.Category == "domain" ? 1 : 2)
                .ThenBy(c => c.Name)
                .ToList();
        }, ct);
    }

    private static BoxOnlyProjectCandidate MakeBoxCandidate(
        string name, string tier, string category, string boxPath) =>
        new(name, tier, category, boxPath, ReadExternalSharedPathsFromBox(boxPath));

    private static List<string> ReadExternalSharedPathsFromBox(string boxProjectPath)
    {
        var candidates = new[]
        {
            Path.Combine(boxProjectPath, "external_shared_paths"),
            Path.Combine(boxProjectPath, ".external_shared_paths")
        };
        try
        {
            foreach (var file in candidates)
                if (File.Exists(file))
                    return File.ReadAllLines(file)
                               .Where(l => !string.IsNullOrWhiteSpace(l))
                               .ToList();
        }
        catch (Exception ex) { Debug.WriteLine($"[ReadExternalSharedPathsFromBox] {ex.Message}"); }
        return [];
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string path)
    {
        try { return Directory.EnumerateDirectories(path); }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SafeEnumerateDirectories] {path}: {ex.Message}");
            return [];
        }
    }

    private List<ProjectInfo> ScanProjects()
    {
        var root = _configService.WorkspaceRoot;
        var projects = new List<ProjectInfo>();
        var now = DateTime.Now;

        if (Directory.Exists(root))
        {
            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                var name = Path.GetFileName(dir);
                if (name == "_INHOUSE" || (!name.StartsWith('_') && !name.StartsWith('.')))
                    projects.Add(BuildProjectInfo(name, dir, "full", "project", now));
            }
        }

        var miniDir = Path.Combine(root, "_mini");
        if (Directory.Exists(miniDir))
        {
            foreach (var dir in Directory.EnumerateDirectories(miniDir))
            {
                var name = Path.GetFileName(dir);
                if (!name.StartsWith('_') && !name.StartsWith('.'))
                    projects.Add(BuildProjectInfo(name, dir, "mini", "project", now));
            }
        }

        var domainsDir = Path.Combine(root, "_domains");
        if (Directory.Exists(domainsDir))
        {
            foreach (var dir in Directory.EnumerateDirectories(domainsDir))
            {
                var name = Path.GetFileName(dir);
                if (!name.StartsWith('_') && !name.StartsWith('.'))
                    projects.Add(BuildProjectInfo(name, dir, "full", "domain", now));
            }
        }

        // 並び順: _INHOUSE → domain-full → domain-mini → regular (full+mini混在, alphabetical)
        return projects
            .OrderBy(p => p.Name == "_INHOUSE" ? 0 :
                          p.Category == "domain" && p.Tier == "full" ? 1 :
                          p.Category == "domain" && p.Tier == "mini" ? 2 : 3)
            .ThenBy(p => p.Name)
            .ToList();
    }

    private static ProjectInfo BuildProjectInfo(string name, string path, string tier, string category, DateTime now)
    {
        var aiCtx = Path.Combine(path, "_ai-context");
        var aiCtxContent = Path.Combine(aiCtx, "context");
        var focusFile = Path.Combine(aiCtxContent, "current_focus.md");
        var summaryFile = Path.Combine(aiCtxContent, "project_summary.md");

        int? focusAge = null;
        if (File.Exists(focusFile))
        {
            var fi = new FileInfo(focusFile);
            focusAge = (int)(now - fi.LastWriteTime).TotalDays;
        }

        int? summaryAge = null;
        if (File.Exists(summaryFile))
        {
            var fi = new FileInfo(summaryFile);
            summaryAge = (int)(now - fi.LastWriteTime).TotalDays;
        }

        var uncommittedRepoPaths = GetUncommittedRepoPaths(path);

        return new ProjectInfo
        {
            Name = name, Tier = tier, Category = category, Path = path,
            AiContextPath = aiCtx, AiContextContentPath = aiCtxContent,
            JunctionShared = GetJunctionStatus(Path.Combine(path, "shared")),
            JunctionObsidian = GetJunctionStatus(Path.Combine(aiCtx, "obsidian_notes")),
            JunctionContext = GetJunctionStatus(aiCtxContent),
            FocusFile = File.Exists(focusFile) ? focusFile : null,
            SummaryFile = File.Exists(summaryFile) ? summaryFile : null,
            FocusAge = focusAge,
            SummaryAge = summaryAge,
            DecisionLogCount = Directory.Exists(Path.Combine(aiCtxContent, "decision_log")) ? Directory.GetFiles(Path.Combine(aiCtxContent, "decision_log"), "*.md").Length : 0,
            FocusHistoryDates = GetFileDates(Path.Combine(aiCtxContent, "focus_history")),
            DecisionLogDates = GetFileDates(Path.Combine(aiCtxContent, "decision_log")),
            ExternalSharedPaths = ReadExternalSharedPaths(path),
            HasUncommittedChanges = uncommittedRepoPaths.Count > 0,
            UncommittedRepoPaths = uncommittedRepoPaths,
            Workstreams = BuildWorkstreams(aiCtxContent, now)
        };
    }

    private static List<string> GetUncommittedRepoPaths(string projectPath)
    {
        var devSource = Path.Combine(projectPath, "development", "source");
        if (!Directory.Exists(devSource)) return [];

        var dirtyPaths = new List<string>();

        try
        {
            foreach (var gitDir in Directory.EnumerateDirectories(devSource, ".git", SearchOption.AllDirectories))
            {
                var repoPath = Path.GetDirectoryName(gitDir);
                if (string.IsNullOrWhiteSpace(repoPath)) continue;
                if (!IsGitRepositoryDirty(repoPath)) continue;

                var relative = repoPath.Length > devSource.Length
                    ? repoPath[devSource.Length..].TrimStart('\\', '/')
                    : ".";

                dirtyPaths.Add(relative.Replace('\\', '/'));
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GetUncommittedRepoPaths] {ex.Message}");
        }

        return dirtyPaths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p)
            .ToList();
    }

    private static bool IsGitRepositoryDirty(string repoPath)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = $"-C \"{repoPath}\" status --porcelain --untracked-files=normal",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                }
            };

            process.Start();
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                Debug.WriteLine($"[IsGitRepositoryDirty] git status failed ({repoPath}): {stderr}");
                return false;
            }

            return !string.IsNullOrWhiteSpace(stdout);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[IsGitRepositoryDirty] {repoPath}: {ex.Message}");
            return false;
        }
    }

    private static List<WorkstreamInfo> BuildWorkstreams(string aiCtxContent, DateTime now)
    {
        var workstreamsDir = Path.Combine(aiCtxContent, "workstreams");
        if (!Directory.Exists(workstreamsDir)) return [];

        var labelMap = ReadWorkstreamLabels(workstreamsDir);
        var result = new List<WorkstreamInfo>();

        try
        {
            foreach (var dir in Directory.EnumerateDirectories(workstreamsDir))
            {
                var id = Path.GetFileName(dir);
                if (id.StartsWith('_') || id.StartsWith('.')) continue;

                var focusFile = Path.Combine(dir, "current_focus.md");
                int? focusAge = null;
                if (File.Exists(focusFile))
                {
                    var fi = new FileInfo(focusFile);
                    focusAge = (int)(now - fi.LastWriteTime).TotalDays;
                }

                labelMap.TryGetValue(id, out var label);

                result.Add(new WorkstreamInfo
                {
                    Id = id,
                    Label = label ?? id,
                    Path = dir,
                    IsClosed = File.Exists(Path.Combine(dir, "_closed")),
                    FocusFile = File.Exists(focusFile) ? focusFile : null,
                    FocusAge = focusAge,
                    DecisionLogCount = Directory.Exists(Path.Combine(dir, "decision_log"))
                        ? Directory.GetFiles(Path.Combine(dir, "decision_log"), "*.md").Length : 0,
                    FocusHistoryDates = GetFileDates(Path.Combine(dir, "focus_history")),
                    DecisionLogDates = GetFileDates(Path.Combine(dir, "decision_log")),
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[BuildWorkstreams] {ex.Message}");
        }

        return result;
    }

    private static Dictionary<string, string> ReadWorkstreamLabels(string workstreamsDir)
    {
        var jsonFile = Path.Combine(workstreamsDir, "workstream.json");
        if (!File.Exists(jsonFile)) return [];

        try
        {
            var json = File.ReadAllText(jsonFile);
            using var doc = JsonDocument.Parse(json);
            var map = new Dictionary<string, string>();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.TryGetProperty("label", out var labelEl)
                    || prop.Value.TryGetProperty("Label", out labelEl))
                    map[prop.Name] = labelEl.GetString() ?? prop.Name;
            }
            return map;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ReadWorkstreamLabels] {ex.Message}");
            return [];
        }
    }

    private static List<DateTime> GetFileDates(string dir)
    {
        var dates = new List<DateTime>();
        if (!Directory.Exists(dir)) return dates;
        try
        {
            foreach (var f in Directory.GetFiles(dir, "*.md"))
            {
                var fi = new FileInfo(f);
                dates.Add(fi.LastWriteTime);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GetFileDates] {ex.Message}");
        }
        return dates;
    }

    /// <summary>shared/ ジャンクション経由で external_shared_paths ファイルを読み込む。</summary>
    private static List<string> ReadExternalSharedPaths(string projectPath)
    {
        var sharedDir = Path.Combine(projectPath, "shared");
        // 新形式 → 旧形式の順で探す
        var candidates = new[]
        {
            Path.Combine(sharedDir, "external_shared_paths"),
            Path.Combine(sharedDir, ".external_shared_paths")
        };
        try
        {
            foreach (var file in candidates)
            {
                if (File.Exists(file))
                    return File.ReadAllLines(file)
                               .Where(l => !string.IsNullOrWhiteSpace(l))
                               .ToList();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ReadExternalSharedPaths] {ex.Message}");
        }
        return [];
    }
}
