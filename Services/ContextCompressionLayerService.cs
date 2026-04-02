using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using ProjectCurator.Models;

namespace ProjectCurator.Services;

public class ContextCompressionLayerService
{
    private const string CclHeader = "## Context Compression Layer";
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly string SkillRoot = Path.Combine(AppContext.BaseDirectory, "Assets", "ContextCompressionLayer", "skills");
    private static readonly Assembly Assembly = typeof(ContextCompressionLayerService).Assembly;
    private static readonly string[] EmbeddedSkillNames = ["project-curator"];
    private readonly ConfigService _configService;

    public ContextCompressionLayerService(ConfigService configService)
    {
        _configService = configService;
    }

    public async Task<ProjectSetupResult> SetupForProjectAsync(
        string projectName,
        string tier,
        string category,
        bool force,
        bool resetSkills = false,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var result = new ProjectSetupResult { Success = true };
            try
            {
                var paths = _configService.LoadSettings();
                var localRoot = Environment.ExpandEnvironmentVariables(paths.LocalProjectsRoot);
                var syncRoot = Environment.ExpandEnvironmentVariables(paths.CloudSyncRoot);
                var obsidianRoot = Environment.ExpandEnvironmentVariables(paths.ObsidianVaultRoot);

                SetupWorkspace(obsidianRoot, localRoot, result.Logs);
                var projectSetupOk = SetupProject(projectName, tier, category, localRoot, syncRoot, obsidianRoot, force, resetSkills, result.Logs);
                if (!projectSetupOk)
                {
                    result.Success = false;
                    result.Message = $"Project setup failed for '{projectName}'.";
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = ex.Message;
                Debug.WriteLine($"[ContextCompressionLayerService] {ex}");
            }

            return result;
        }, ct);
    }

    private static void SetupWorkspace(string obsidianRoot, string localRoot, List<string> logs)
    {
        var workspaceContext = Path.Combine(localRoot, ".context");
        EnsureDirectory(workspaceContext, logs);
        WriteIfMissing(Path.Combine(workspaceContext, "workspace_summary.md"), WorkspaceSummaryTemplate, logs);
        WriteIfMissing(Path.Combine(workspaceContext, "current_focus.md"), CurrentFocusTemplate, logs);
        WriteIfMissing(Path.Combine(workspaceContext, "active_projects.md"), ActiveProjectsTemplate, logs);
        WriteIfMissing(Path.Combine(workspaceContext, "tensions.md"), TensionsTemplate, logs);

        var globalAiContext = Path.Combine(obsidianRoot, "ai-context");
        EnsureDirectory(globalAiContext, logs);
        EnsureDirectory(Path.Combine(globalAiContext, "tech-patterns"), logs);
        EnsureDirectory(Path.Combine(globalAiContext, "lessons-learned"), logs);
    }

    private static bool SetupProject(
        string projectName,
        string tier,
        string category,
        string localRoot,
        string boxRoot,
        string obsidianRoot,
        bool force,
        bool resetSkills,
        List<string> logs)
    {
        logs.Add($"[DEBUG] SetupProject: name={projectName} tier={tier} category={category} force={force}");
        var categoryPrefix = category == "domain" ? "_domains" : "";
        var projectSubPath = tier == "mini"
            ? Path.Combine(categoryPrefix, "_mini", projectName)
            : Path.Combine(categoryPrefix, projectName);

        var projectRoot = Path.Combine(localRoot, projectSubPath);
        var boxProjectRoot = Path.Combine(boxRoot, projectSubPath);
        var obsidianProjectRoot = projectName == "_INHOUSE"
            ? Path.Combine(obsidianRoot, "_INHOUSE")
            : Path.Combine(obsidianRoot, "Projects", projectSubPath);
        var obsidianAiContext = Path.Combine(obsidianProjectRoot, "ai-context");

        if (!Directory.Exists(projectRoot))
        {
            logs.Add($"[ERROR] Project not found: {projectRoot}");
            return false;
        }

        EnsureDirectory(obsidianAiContext, logs);
        EnsureDirectory(Path.Combine(obsidianAiContext, "decision_log"), logs);
        EnsureDirectory(Path.Combine(obsidianAiContext, "focus_history"), logs);

        WriteIfMissing(Path.Combine(obsidianAiContext, "project_summary.md"), ProjectSummaryTemplate, logs);
        WriteIfMissing(Path.Combine(obsidianAiContext, "current_focus.md"), CurrentFocusTemplate, logs);
        WriteIfMissing(Path.Combine(obsidianAiContext, "tensions.md"), TensionsTemplate, logs);
        WriteIfMissing(Path.Combine(obsidianAiContext, "file_map.md"), BuildFileMap(projectName, tier), logs);
        WriteIfMissing(Path.Combine(obsidianAiContext, "decision_log", "TEMPLATE.md"), DecisionLogTemplate, logs);

        var contextJunction = Path.Combine(projectRoot, "_ai-context", "context");
        EnsureDirectory(Path.Combine(projectRoot, "_ai-context"), logs);
        EnsureJunction(contextJunction, obsidianAiContext, logs);

        var targets = new[]
        {
            Path.Combine(projectRoot, "AGENTS.md"),
            Path.Combine(projectRoot, "shared", "AGENTS.md")
        };
        foreach (var mdPath in targets)
        {
            AppendCclSectionIfNeeded(mdPath, logs);
        }

        try
        {
            SetupCliSkills(projectRoot, boxProjectRoot, force, resetSkills, logs);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SetupCliSkills] {ex}");
            logs.Add($"[WARN] Skill deployment encountered an error: {ex.Message}");
        }

        SetupCliSkillsJunctions(projectRoot, boxProjectRoot, force, logs);
        return true;
    }

    private static void SetupCliSkills(string localProjectRoot, string boxProjectRoot, bool force, bool resetSkills, List<string> logs)
    {
        var skillFolders = Directory.Exists(SkillRoot)
            ? Directory.GetDirectories(SkillRoot)
            : [];

        logs.Add($"[DEBUG] boxProjectRoot={boxProjectRoot}");
        if (skillFolders.Length > 0)
        {
            foreach (var cli in new[] { ".claude", ".codex", ".gemini", ".github" })
            {
                var dstSkillsDir = Path.Combine(boxProjectRoot, cli, "skills");

                if (resetSkills && Directory.Exists(dstSkillsDir))
                {
                    try
                    {
                        Directory.Delete(dstSkillsDir, recursive: true);
                        logs.Add($"[RESET] {cli}/skills/ (deleted before re-deploy)");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[SetupCliSkills] Reset delete failed: {ex.Message}");
                        logs.Add($"[WARN] {cli}/skills/ could not be reset: {ex.Message}");
                    }
                }

                EnsureDirectory(dstSkillsDir, logs);
                foreach (var srcSkillDir in skillFolders)
                {
                    var skillName = Path.GetFileName(srcSkillDir);
                    var dstSkillDir = Path.Combine(dstSkillsDir, skillName);
                    if (!Directory.Exists(dstSkillDir))
                    {
                        CopyDirectory(srcSkillDir, dstSkillDir);
                        logs.Add($"[CREATE] {cli}/skills/{skillName}");
                        continue;
                    }

                    if (force)
                    {
                        try
                        {
                            Directory.Delete(dstSkillDir, recursive: true);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[SetupCliSkills] Delete failed, falling back to overwrite: {ex.Message}");
                        }

                        CopyDirectory(srcSkillDir, dstSkillDir);
                        logs.Add($"[UPDATE] {cli}/skills/{skillName} (overwritten)");
                    }
                    else
                    {
                        logs.Add($"[SKIP] {cli}/skills/{skillName} already exists.");
                    }
                }
            }

            return;
        }

        logs.Add("[INFO] Skills folder on disk was not found or empty. Using embedded skill assets.");
        logs.Add($"[DEBUG] boxProjectRoot={boxProjectRoot}");
        foreach (var cli in new[] { ".claude", ".codex", ".gemini", ".github" })
        {
            var dstSkillsDir = Path.Combine(boxProjectRoot, cli, "skills");

            if (resetSkills && Directory.Exists(dstSkillsDir))
            {
                try
                {
                    Directory.Delete(dstSkillsDir, recursive: true);
                    logs.Add($"[RESET] {cli}/skills/ (deleted before re-deploy)");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SetupCliSkills] Reset delete failed: {ex.Message}");
                    logs.Add($"[WARN] {cli}/skills/ could not be reset: {ex.Message}");
                }
            }

            EnsureDirectory(dstSkillsDir, logs);
            foreach (var skillName in EmbeddedSkillNames)
            {
                var skillFiles = ReadEmbeddedSkillFiles(skillName);
                if (skillFiles.Count == 0)
                {
                    logs.Add($"[WARN] Skill template not found: {skillName}");
                    continue;
                }

                var dstSkillDir = Path.Combine(dstSkillsDir, skillName);
                var existedBefore = File.Exists(Path.Combine(dstSkillDir, "SKILL.md"));

                if (existedBefore && !force)
                {
                    logs.Add($"[SKIP] {cli}/skills/{skillName} already exists.");
                    continue;
                }

                foreach (var (relativePath, content) in skillFiles)
                {
                    var dstFile = Path.Combine(dstSkillDir, relativePath);
                    EnsureDirectory(Path.GetDirectoryName(dstFile)!, logs);
                    File.WriteAllText(dstFile, content, Utf8NoBom);
                }

                logs.Add(existedBefore
                    ? $"[UPDATE] {cli}/skills/{skillName} (overwritten)"
                    : $"[CREATE] {cli}/skills/{skillName}");
            }
        }
    }

    private static void SetupCliSkillsJunctions(string localProjectRoot, string boxProjectRoot, bool force, List<string> logs)
    {
        foreach (var cli in new[] { ".claude", ".codex", ".gemini", ".github" })
        {
            var localPath = Path.Combine(localProjectRoot, cli);
            var boxPath = Path.Combine(boxProjectRoot, cli);

            // Directory.Exists は壊れたジャンクション (ターゲット不在) で false を返す。
            // File.GetAttributes ならターゲットを辿らずにエントリ自体の属性を取得できる。
            FileAttributes localAttr;
            bool localEntryExists;
            try
            {
                localAttr = File.GetAttributes(localPath);
                localEntryExists = true;
            }
            catch
            {
                localAttr = default;
                localEntryExists = false;
            }

            logs.Add($"[DEBUG] {cli}: localExists={localEntryExists} attr={localAttr} force={force}");

            if (localEntryExists)
            {
                if ((localAttr & FileAttributes.ReparsePoint) != 0)
                {
                    // ReparsePoint = ジャンクション: ターゲットが正しいか検証
                    string? actualFull = null;
                    try
                    {
                        var target = Directory.ResolveLinkTarget(localPath, returnFinalTarget: false);
                        actualFull = target != null ? Path.GetFullPath(target.FullName) : null;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[SetupCliSkillsJunctions] ResolveLinkTarget failed: {ex.Message}");
                    }

                    var expectedFull = Path.GetFullPath(boxPath);
                    logs.Add($"[DEBUG] {cli} junction target: actual={actualFull ?? "null"}");
                    logs.Add($"[DEBUG] {cli} junction expected: {expectedFull}");
                    if (actualFull == expectedFull)
                    {
                        logs.Add($"[SKIP] {cli} junction already exists.");
                        continue;
                    }

                    // ターゲットが違う or 壊れている (文字化けパス含む) → 削除して再作成
                    logs.Add($"[WARN] {cli} junction has wrong/broken target (actual={actualFull ?? "null"}, expected={expectedFull}). Recreating...");
                    try { Directory.Delete(localPath); }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[SetupCliSkillsJunctions] Delete broken junction failed: {ex}");
                        logs.Add($"[WARN] {cli} could not delete broken junction; skipped.");
                        continue;
                    }
                }
                else
                {
                    if (!force)
                    {
                        logs.Add($"[WARN] {cli} exists as a regular directory; skipped junction creation. Use 'Overwrite existing skills' to replace.");
                        continue;
                    }
                    // force=true: 既存の実ディレクトリを Box にマージしてから junction に置き換える
                    logs.Add($"[INFO] {cli} is a regular directory. Merging to BOX and replacing with junction...");
                    var boxPath2 = Path.Combine(boxProjectRoot, cli);
                    try
                    {
                        if (Directory.Exists(localPath))
                            CopyDirectory(localPath, boxPath2); // Box に内容をマージ (上書きはしない)
                        Directory.Delete(localPath, recursive: true);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[SetupCliSkillsJunctions] Merge/Delete regular dir failed: {ex}");
                        logs.Add($"[WARN] {cli} could not replace regular directory; skipped. ({ex.Message})");
                        continue;
                    }
                }
            }

            // Box 側が存在しない場合は作成してからジャンクションを張る
            if (!Directory.Exists(boxPath))
            {
                try
                {
                    Directory.CreateDirectory(boxPath);
                    logs.Add($"[CREATE] {boxPath}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SetupCliSkillsJunctions] Cannot create box dir: {ex}");
                    logs.Add($"[INFO] {cli} source not found in BOX and could not be created; skipped.");
                    continue;
                }
            }

            EnsureJunction(localPath, boxPath, logs);
        }
    }

    private static void AppendCclSectionIfNeeded(string mdPath, List<string> logs)
    {
        if (!File.Exists(mdPath))
        {
            logs.Add($"[INFO] {mdPath} not found; skipped CCL append.");
            return;
        }

        try
        {
            var content = File.ReadAllText(mdPath, new UTF8Encoding(false));
            if (content.Contains(CclHeader, StringComparison.Ordinal))
            {
                logs.Add($"[SKIP] {mdPath} already contains CCL section.");
                return;
            }

            var appended = content.TrimEnd() + Environment.NewLine + Environment.NewLine + LoadCclSection() + Environment.NewLine;
            File.WriteAllText(mdPath, appended, new UTF8Encoding(false));
            logs.Add($"[UPDATE] Appended CCL section to {mdPath}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AppendCclSectionIfNeeded] {ex}");
            logs.Add($"[WARN] Failed to append CCL section: {mdPath}");
        }
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destinationFile = Path.Combine(destinationDir, Path.GetFileName(file));
            File.Copy(file, destinationFile, overwrite: true);
        }

        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            var destinationSubDir = Path.Combine(destinationDir, Path.GetFileName(subDir));
            CopyDirectory(subDir, destinationSubDir);
        }
    }

    private static string LoadCclSection()
    {
        try
        {
            var snippet = ReadCclAssetText("templates/CLAUDE_MD_SNIPPET.md");
            if (string.IsNullOrWhiteSpace(snippet))
                return CclSectionFallback;

            var match = Regex.Match(snippet, "(?s)```markdown\\r?\\n(.*?)\\r?\\n```");
            return match.Success ? match.Groups[1].Value : CclSectionFallback;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LoadCclSection] {ex}");
            return CclSectionFallback;
        }
    }

    public static string BuildAgentsTemplate(string projectName)
    {
        try
        {
            var template = ReadCclAssetText("templates/AGENTS.md");
            if (string.IsNullOrWhiteSpace(template))
            {
                return $$"""
# {{projectName}} - AI Agent Instructions

- Project: {{projectName}} / Created: {{DateTime.Now:yyyy-MM-dd}}
""";
            }

            return template
                .Replace("{{PROJECT_NAME}}", projectName)
                .Replace("{{CREATION_DATE}}", DateTime.Now.ToString("yyyy-MM-dd"));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[BuildAgentsTemplate] {ex}");
            return $$"""
# {{projectName}} - AI Agent Instructions

- Project: {{projectName}} / Created: {{DateTime.Now:yyyy-MM-dd}}
""";
        }
    }

    /// <summary>
    /// Reads all files for a skill using MANIFEST for discovery, ReadCclAssetText for content.
    /// MANIFEST lists relative paths (e.g. "reference/decision-log.md"), one per line.
    /// </summary>
    private static List<(string RelativePath, string Content)> ReadEmbeddedSkillFiles(string skillName)
    {
        var manifest = ReadCclAssetText($"skills/{skillName}/MANIFEST");
        if (manifest != null)
        {
            var results = new List<(string, string)>();
            foreach (var line in manifest.Split('\n'))
            {
                var relativePath = line.Trim().Replace('/', Path.DirectorySeparatorChar);
                if (string.IsNullOrEmpty(relativePath)) continue;
                var content = ReadCclAssetText($"skills/{skillName}/{line.Trim()}");
                if (content != null) results.Add((relativePath, content));
            }
            return results;
        }

        // Fallback for skills without MANIFEST: try SKILL.md only
        var skillContent = ReadCclAssetText($"skills/{skillName}/SKILL.md");
        if (skillContent != null)
            return [("SKILL.md", skillContent)];

        return [];
    }

    private static string? ReadCclAssetText(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/').TrimStart('/');
        var suffix = $"Assets.ContextCompressionLayer.{normalized.Replace('/', '.')}";
        var resourceNames = Assembly.GetManifestResourceNames();
        var resourceName = Array.Find(
            resourceNames,
            n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

        if (resourceName == null)
        {
            var normalizedSuffix = NormalizeResourceKey(suffix);
            resourceName = Array.Find(
                resourceNames,
                n => NormalizeResourceKey(n).EndsWith(normalizedSuffix, StringComparison.Ordinal));
        }

        if (resourceName != null)
        {
            using var stream = Assembly.GetManifestResourceStream(resourceName);
            if (stream != null)
            {
                using var reader = new StreamReader(stream, Utf8NoBom, detectEncodingFromByteOrderMarks: true);
                return reader.ReadToEnd();
            }
        }

        var fallbackPath = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "ContextCompressionLayer",
            normalized.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(fallbackPath) ? File.ReadAllText(fallbackPath, Utf8NoBom) : null;
    }

    private static string NormalizeResourceKey(string value)
    {
        var chars = value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray();
        return new string(chars);
    }

    private static void EnsureDirectory(string path, List<string> logs)
    {
        if (Directory.Exists(path))
            return;

        Directory.CreateDirectory(path);
        logs.Add($"[CREATE] {path}");
    }

    private static void EnsureJunction(string linkPath, string targetPath, List<string> logs)
    {
        // File.GetAttributes はターゲットを辿らないのでジャンクションが壊れていても属性を取得できる
        FileAttributes linkAttr;
        bool linkEntryExists;
        try
        {
            linkAttr = File.GetAttributes(linkPath);
            linkEntryExists = true;
        }
        catch
        {
            linkAttr = default;
            linkEntryExists = false;
        }

        if (linkEntryExists)
        {
            if ((linkAttr & FileAttributes.ReparsePoint) != 0)
            {
                try
                {
                    var resolved = Directory.ResolveLinkTarget(linkPath, returnFinalTarget: false);
                    var expectedFull = Path.GetFullPath(targetPath);
                    var actualFull = resolved != null ? Path.GetFullPath(resolved.FullName) : null;
                    if (actualFull == expectedFull)
                    {
                        logs.Add($"[SKIP] Junction exists: {linkPath}");
                        return;
                    }
                    logs.Add($"[WARN] Junction has wrong target (actual={actualFull ?? "null"}). Recreating...");
                    Directory.Delete(linkPath);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[EnsureJunction] ResolveLinkTarget/Delete failed: {ex}");
                    logs.Add($"[WARN] Could not validate/remove existing junction: {linkPath}. Skipped.");
                    return;
                }
            }
            else
            {
                logs.Add($"[WARN] {linkPath} exists as a regular directory; skipped junction creation.");
                return;
            }
        }
        // Box Drive のキャッシュ不整合で Directory.Exists が false を返す場合があるため、
        // 存在しなければ作成を試みる (作成失敗時のみスキップ)
        if (!Directory.Exists(targetPath))
        {
            try
            {
                Directory.CreateDirectory(targetPath);
                logs.Add($"[CREATE] {targetPath} (for junction target)");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EnsureJunction] Cannot create target dir: {ex}");
                logs.Add($"[WARN] Junction target not found and could not be created: {targetPath}");
                return;
            }
        }

        try
        {
            // Unicode パス (日本語等) を含む場合も正確に扱うため PowerShell + EncodedCommand を使用
            // cmd.exe の mklink /j は OEM コードページ経由で Unicode を失うため使わない
            var psCommand = $"New-Item -ItemType Junction -Path '{linkPath}' -Target '{targetPath}' -Force | Out-Null";
            var encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(psCommand));

            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NonInteractive -NoProfile -EncodedCommand {encoded}",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            string stdout = process?.StandardOutput.ReadToEnd() ?? "";
            string stderr = process?.StandardError.ReadToEnd() ?? "";
            process?.WaitForExit();

            if (process is { ExitCode: 0 })
            {
                logs.Add($"[CREATE] {linkPath} -> {targetPath}");
            }
            else
            {
                var detail = (stdout + stderr).Trim();
                Debug.WriteLine($"[EnsureJunction] PowerShell failed: exit={process?.ExitCode} out={stdout} err={stderr}");
                logs.Add($"[WARN] Failed to create junction: {linkPath}" +
                         (string.IsNullOrEmpty(detail) ? "" : $" ({detail})"));
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[EnsureJunction] {ex}");
            logs.Add($"[WARN] Failed to create junction: {linkPath} ({ex.Message})");
        }
    }

    private static void WriteIfMissing(string path, string content, List<string> logs)
    {
        if (File.Exists(path))
        {
            logs.Add($"[SKIP] {path} already exists.");
            return;
        }

        File.WriteAllText(path, content, new UTF8Encoding(false));
        logs.Add($"[CREATE] {path}");
    }

    private static string BuildFileMap(string projectName, string tier)
    {
        return FileMapTemplate
            .Replace("{{PROJECT_NAME}}", projectName)
            .Replace("{{TIER}}", tier)
            .Replace("{{CREATION_DATE}}", DateTime.Now.ToString("yyyy-MM-dd"));
    }

    internal const string ProjectSummaryTemplate = """
# Project Summary

## Overview

- Project name:
- Goal:
- Period:
- Current phase:

## Tech Stack

| Category | Technology |
|---------|------------|
| Language | |
| DB | |
| Infrastructure | |

## Architecture

```
(Text architecture sketch)
```

## Notes

- 
""";

    internal const string CurrentFocusTemplate = """
# Focus

## Currently Doing

- 

## Recent Updates

- 

## Next Actions

- 

## Notes

- 

---

Last Updated: YYYY-MM-DD
""";

    private const string TensionsTemplate = """
# Tensions

## Open technical questions

- 

## Unresolved trade-offs

- 

## Risks and concerns

- 

---
Last Update: YYYY-MM-DD
""";

    private const string DecisionLogTemplate = """
# Decision: {Title}

> Date: YYYY-MM-DD
> Status: Confirmed / Tentative
> Trigger: AI session / Meeting / Solo decision

## Context

## Options

### Option A: {Name}

- Pros:
- Cons:

### Option B: {Name}

- Pros:
- Cons:

## Chosen

**Option X: {Name}**

## Why

## Risks

- 

## Revisit Trigger

- 
""";

    private const string WorkspaceSummaryTemplate = """
# Workspace Summary

## About me

- Role:
- Primary stack:

## Working principles

- 

## Tools

| Category | Tool |
|---------|------|
| AI | |
| Editor | |
| Knowledge | |
| Task management | |
| Shared storage | |
""";

    private const string ActiveProjectsTemplate = """
# Active Projects

## Full Tier

### {ProjectName}

- Status: In progress / Paused / Blocked
- Phase:
- Note:

## Mini Tier

### {ProjectName}

- Status:
- Note:
""";

    private const string FileMapTemplate = """
# File Map - {{PROJECT_NAME}}

<!--
  Tier: {{TIER}} (full / mini)
  Created: {{CREATION_DATE}}
  Updated: {{CREATION_DATE}}
-->

## Junction Mapping

| Local path | Link target | Purpose |
|------------|-------------|---------|
| `_ai-context/context/` | `[obsidianVaultRoot]/Projects/{{PROJECT_NAME}}/ai-context/` | AI context |
| `_ai-context/obsidian_notes/` | `[obsidianVaultRoot]/Projects/{{PROJECT_NAME}}/` | Obsidian notes |
| `shared/` | `[cloudSyncRoot]/Projects/{{PROJECT_NAME}}/` | Shared assets |

## Primary files

| Path | Purpose | Priority |
|------|---------|----------|
| `_ai-context/context/project_summary.md` | Project overview and goals | Read first |
| `_ai-context/context/current_focus.md` | Current focus and near-term tasks | Every session |
| `_ai-context/context/decision_log/` | Decision records (latest first) | On key decisions |
| `_ai-context/context/file_map.md` | This map | When needed |
""";

    private const string CclSectionFallback = """
## Context Compression Layer

### Session Start

Before responding to the first message, read in order:

1. `_ai-context/context/current_focus.md`
   (shared_work mode: prefer `workstreams/<workstreamId>/current_focus.md` when the
   workstream is known; fall back to the root `current_focus.md`)
2. `_ai-context/context/project_summary.md`
3. `_ai-context/context/tensions.md` (if it exists)
4. `_ai-context/obsidian_notes/asana-tasks.md` (if it exists)
5. Other files on demand only.

After reading, present a 1-2 line summary of open items (factor in any tensions).
If focus is 3+ days old, ask about progress once.
If any context file is oversized, suggest archiving to `focus_history/`.

### Active Behaviors

Decision logging, session-end focus updates, Obsidian knowledge integration,
and Asana focus sync are handled by the `/project-curator` skill.
Invoke `/project-curator` at the start of a session to activate these behaviors.
""";
}
