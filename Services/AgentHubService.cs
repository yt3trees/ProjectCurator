using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ProjectCurator.Models;

namespace ProjectCurator.Services;

public class AgentHubService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly string[] BuiltInSkillNames = ["project-curator"];
    private static readonly Assembly _assembly = typeof(AgentHubService).Assembly;

    private readonly ConfigService _configService;

    public string AgentHubDir => Path.Combine(ResolveAgentHubConfigDir(), "agent_hub");
    public string AgentsDir => Path.Combine(AgentHubDir, "agents");
    public string RulesDir => Path.Combine(AgentHubDir, "rules");
    public string SkillsDir => Path.Combine(AgentHubDir, "skills");

    public AgentHubService(ConfigService configService)
    {
        _configService = configService;
    }

    // ─── Agents ───────────────────────────────────────────────────────────

    public List<AgentDefinition> GetAgentDefinitions()
    {
        if (!Directory.Exists(AgentsDir)) return [];

        var result = new List<AgentDefinition>();
        foreach (var jsonFile in Directory.GetFiles(AgentsDir, "*.json"))
        {
            try
            {
                var content = File.ReadAllText(jsonFile, new UTF8Encoding(false));
                var def = JsonSerializer.Deserialize<AgentDefinition>(content, JsonOptions);
                if (def != null) result.Add(def);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AgentHubService] Failed to load agent {jsonFile}: {ex.Message}");
            }
        }
        return [.. result.OrderBy(d => d.Name)];
    }

    public string GetAgentContent(AgentDefinition def)
    {
        if (string.IsNullOrEmpty(def.ContentFile)) return "";
        var path = Path.Combine(AgentsDir, def.ContentFile);
        return File.Exists(path) ? File.ReadAllText(path, new UTF8Encoding(false)) : "";
    }

    // Returns body-only and extracted extra frontmatter for the Edit dialog.
    public (string Body, string ExtraFrontmatter) GetAgentContentForEdit(AgentDefinition def)
    {
        var raw = GetAgentContent(def);
        var trimmed = raw.TrimStart();
        if (!trimmed.StartsWith("---\n", StringComparison.Ordinal) &&
            !trimmed.StartsWith("---\r\n", StringComparison.Ordinal))
            return (raw, def.FrontmatterClaude);

        var match = Regex.Match(trimmed, @"\A---\r?\n([\s\S]*?)\r?\n---\r?\n?", RegexOptions.CultureInvariant);
        if (!match.Success)
            return (raw, def.FrontmatterClaude);

        var fmBlock = match.Groups[1].Value;
        var body = trimmed[match.Length..];
        var extraLines = fmBlock.Replace("\r\n", "\n")
            .Split('\n')
            .Where(l => !string.IsNullOrWhiteSpace(l) &&
                        !Regex.IsMatch(l, @"^(name|description)\s*:", RegexOptions.IgnoreCase));
        var extra = string.Join("\n", extraLines);
        return (body.TrimStart('\r', '\n'), !string.IsNullOrWhiteSpace(extra) ? extra : def.FrontmatterClaude);
    }

    public void SaveAgentDefinition(AgentDefinition def, string content)
    {
        EnsureDir(AgentsDir);
        if (string.IsNullOrEmpty(def.Id))
        {
            def.Id = def.Name.ToLowerInvariant()
                .Replace(" ", "-")
                .Replace("/", "-")
                .Replace("\\", "-");
            def.CreatedAt = DateTimeOffset.Now;
        }
        def.ContentFile = def.Id + ".md";
        def.UpdatedAt = DateTimeOffset.Now;

        var jsonPath = Path.Combine(AgentsDir, def.Id + ".json");
        var mdPath = Path.Combine(AgentsDir, def.ContentFile);

        File.WriteAllText(jsonPath, JsonSerializer.Serialize(def, JsonOptions), new UTF8Encoding(false));
        File.WriteAllText(mdPath, EmbedFrontmatter(def, content), new UTF8Encoding(false));
    }

    public void DeleteAgentDefinition(string agentId)
    {
        TryDelete(Path.Combine(AgentsDir, agentId + ".json"));
        TryDelete(Path.Combine(AgentsDir, agentId + ".md"));
    }

    // ─── Rules ────────────────────────────────────────────────────────────

    public List<ContextRuleDefinition> GetRuleDefinitions()
    {
        if (!Directory.Exists(RulesDir)) return [];

        var result = new List<ContextRuleDefinition>();
        foreach (var jsonFile in Directory.GetFiles(RulesDir, "*.json"))
        {
            try
            {
                var content = File.ReadAllText(jsonFile, new UTF8Encoding(false));
                var def = JsonSerializer.Deserialize<ContextRuleDefinition>(content, JsonOptions);
                if (def != null) result.Add(def);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AgentHubService] Failed to load rule {jsonFile}: {ex.Message}");
            }
        }
        return [.. result.OrderBy(d => d.Name)];
    }

    public string GetRuleContent(ContextRuleDefinition def)
    {
        if (string.IsNullOrEmpty(def.ContentFile)) return "";
        var path = Path.Combine(RulesDir, def.ContentFile);
        return File.Exists(path) ? File.ReadAllText(path, new UTF8Encoding(false)) : "";
    }

    public void SaveRuleDefinition(ContextRuleDefinition def, string content)
    {
        EnsureDir(RulesDir);
        if (string.IsNullOrEmpty(def.Id))
        {
            def.Id = def.Name.ToLowerInvariant()
                .Replace(" ", "-")
                .Replace("/", "-")
                .Replace("\\", "-");
            def.CreatedAt = DateTimeOffset.Now;
        }
        def.ContentFile = def.Id + ".md";
        def.UpdatedAt = DateTimeOffset.Now;

        var jsonPath = Path.Combine(RulesDir, def.Id + ".json");
        var mdPath = Path.Combine(RulesDir, def.ContentFile);

        File.WriteAllText(jsonPath, JsonSerializer.Serialize(def, JsonOptions), new UTF8Encoding(false));
        File.WriteAllText(mdPath, content, new UTF8Encoding(false));
    }

    public void DeleteRuleDefinition(string ruleId)
    {
        TryDelete(Path.Combine(RulesDir, ruleId + ".json"));
        TryDelete(Path.Combine(RulesDir, ruleId + ".md"));
    }

    // ─── Skills ───────────────────────────────────────────────────────────

    public List<SkillDefinition> GetSkillDefinitions()
    {
        if (!Directory.Exists(SkillsDir)) return [];

        var result = new List<SkillDefinition>();
        foreach (var skillDir in Directory.GetDirectories(SkillsDir))
        {
            var metaPath = Path.Combine(skillDir, "meta.json");
            if (!File.Exists(metaPath)) continue;
            try
            {
                var json = File.ReadAllText(metaPath, new UTF8Encoding(false));
                var def = JsonSerializer.Deserialize<SkillDefinition>(json, JsonOptions);
                if (def != null)
                {
                    def.ContentDirectory = skillDir;
                    result.Add(def);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AgentHubService] Failed to load skill {skillDir}: {ex.Message}");
            }
        }
        return [.. result.OrderBy(d => d.IsBuiltIn ? 0 : 1).ThenBy(d => d.Name)];
    }

    public string GetSkillContent(SkillDefinition def)
    {
        var skillMdPath = Path.Combine(def.ContentDirectory, "SKILL.md");
        return File.Exists(skillMdPath) ? File.ReadAllText(skillMdPath, new UTF8Encoding(false)) : "";
    }

    public void SaveSkillDefinition(SkillDefinition def, string skillMdContent)
    {
        if (def.IsBuiltIn)
            throw new InvalidOperationException("Cannot modify a built-in skill.");

        EnsureDir(SkillsDir);
        if (string.IsNullOrEmpty(def.Id))
        {
            def.Id = def.Name.ToLowerInvariant()
                .Replace(" ", "-").Replace("/", "-").Replace("\\", "-");
            def.CreatedAt = DateTimeOffset.Now;
        }

        var skillDir = Path.Combine(SkillsDir, def.Id);
        EnsureDir(skillDir);
        def.ContentDirectory = skillDir;
        def.UpdatedAt = DateTimeOffset.Now;

        File.WriteAllText(Path.Combine(skillDir, "meta.json"),
            JsonSerializer.Serialize(def, JsonOptions), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(skillDir, "SKILL.md"),
            skillMdContent, new UTF8Encoding(false));
    }

    public void DeleteSkillDefinition(string skillId)
    {
        var skillDir = Path.Combine(SkillsDir, skillId);
        var metaPath = Path.Combine(skillDir, "meta.json");
        if (File.Exists(metaPath))
        {
            try
            {
                var json = File.ReadAllText(metaPath, new UTF8Encoding(false));
                var def = JsonSerializer.Deserialize<SkillDefinition>(json, JsonOptions);
                if (def?.IsBuiltIn == true)
                    throw new InvalidOperationException("Cannot delete a built-in skill.");
            }
            catch (InvalidOperationException) { throw; }
            catch { }
        }
        TryDeleteDirectory(skillDir);
    }

    public void EnsureBuiltInSkills()
    {
        EnsureDir(SkillsDir);
        foreach (var skillName in BuiltInSkillNames)
        {
            var skillDir = Path.Combine(SkillsDir, skillName);
            EnsureDir(skillDir);

            var skillFiles = ReadEmbeddedSkillFiles(skillName);
            foreach (var (relativePath, content) in skillFiles)
            {
                var targetPath = Path.Combine(skillDir,
                    relativePath.Replace('/', Path.DirectorySeparatorChar));
                var targetParent = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrWhiteSpace(targetParent)) EnsureDir(targetParent);
                File.WriteAllText(targetPath, content, new UTF8Encoding(false));
            }

            var metaPath = Path.Combine(skillDir, "meta.json");
            SkillDefinition meta;
            if (File.Exists(metaPath))
            {
                try
                {
                    meta = JsonSerializer.Deserialize<SkillDefinition>(
                        File.ReadAllText(metaPath, new UTF8Encoding(false)), JsonOptions)
                        ?? new SkillDefinition();
                }
                catch { meta = new SkillDefinition(); }
            }
            else
            {
                meta = new SkillDefinition { CreatedAt = DateTimeOffset.Now };
            }

            meta.Id = skillName;
            meta.Name = SkillIdToDisplayName(skillName);
            meta.IsBuiltIn = true;
            meta.ContentDirectory = skillDir;
            meta.UpdatedAt = DateTimeOffset.Now;
            if (string.IsNullOrWhiteSpace(meta.Description))
                meta.Description = "Built-in skill provided by ProjectCurator.";

            File.WriteAllText(metaPath,
                JsonSerializer.Serialize(meta, JsonOptions), new UTF8Encoding(false));
        }
    }

    // ─── Phase 5 helpers ──────────────────────────────────────────────────

    public void ExportLibraryZip(string outputZipPath)
    {
        EnsureDir(AgentHubDir);
        var outDir = Path.GetDirectoryName(outputZipPath);
        if (!string.IsNullOrWhiteSpace(outDir))
            EnsureDir(outDir);

        if (File.Exists(outputZipPath))
            File.Delete(outputZipPath);

        using var zip = ZipFile.Open(outputZipPath, ZipArchiveMode.Create);
        AddDirectoryToZip(zip, AgentsDir, "agents");
        AddDirectoryToZip(zip, RulesDir, "rules");
        AddDirectoryToZip(zip, SkillsDir, "skills");
    }

    public void ImportLibraryZip(string sourceZipPath)
    {
        if (!File.Exists(sourceZipPath))
            throw new FileNotFoundException("ZIP file not found.", sourceZipPath);

        EnsureDir(AgentHubDir);
        EnsureDir(AgentsDir);
        EnsureDir(RulesDir);
        EnsureDir(SkillsDir);

        var tempDir = Path.Combine(Path.GetTempPath(), $"projectcurator_agenthub_import_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            ZipFile.ExtractToDirectory(sourceZipPath, tempDir, overwriteFiles: true);

            var extractedAgentsDir = FindFirstExistingDirectory(
                Path.Combine(tempDir, "agents"),
                Path.Combine(tempDir, "agent_hub", "agents"));

            var extractedRulesDir = FindFirstExistingDirectory(
                Path.Combine(tempDir, "rules"),
                Path.Combine(tempDir, "agent_hub", "rules"));

            var extractedSkillsDir = FindFirstExistingDirectory(
                Path.Combine(tempDir, "skills"),
                Path.Combine(tempDir, "agent_hub", "skills"));

            if (extractedAgentsDir == null && extractedRulesDir == null && extractedSkillsDir == null)
                throw new InvalidDataException("Invalid library ZIP. Expected 'agents/', 'rules/', or 'skills/' folder.");

            if (extractedAgentsDir != null)
                CopyDirectoryContents(extractedAgentsDir, AgentsDir);
            if (extractedRulesDir != null)
                CopyDirectoryContents(extractedRulesDir, RulesDir);
            if (extractedSkillsDir != null)
            {
                // Import skills but always enforce IsBuiltIn for known built-in skill names.
                foreach (var srcSkillDir in Directory.GetDirectories(extractedSkillsDir))
                {
                    var skillId = Path.GetFileName(srcSkillDir);
                    var dstSkillDir = Path.Combine(SkillsDir, skillId);
                    CopyDirectoryContents(srcSkillDir, dstSkillDir);

                    // Preserve IsBuiltIn flag for known built-in skills
                    if (Array.Exists(BuiltInSkillNames, n => n == skillId))
                    {
                        var metaPath = Path.Combine(dstSkillDir, "meta.json");
                        if (File.Exists(metaPath))
                        {
                            try
                            {
                                var meta = JsonSerializer.Deserialize<SkillDefinition>(
                                    File.ReadAllText(metaPath, new UTF8Encoding(false)), JsonOptions);
                                if (meta != null)
                                {
                                    meta.IsBuiltIn = true;
                                    File.WriteAllText(metaPath,
                                        JsonSerializer.Serialize(meta, JsonOptions), new UTF8Encoding(false));
                                }
                            }
                            catch { }
                        }
                    }
                }
            }
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }


    public ImportDirectoryResult ImportFromDirectory(string dirPath, bool overwrite)
    {
        if (!Directory.Exists(dirPath))
            throw new DirectoryNotFoundException($"Directory not found: {dirPath}");

        EnsureDir(AgentHubDir);
        EnsureDir(AgentsDir);
        EnsureDir(SkillsDir);

        var result = new ImportDirectoryResult();

        // Agents
        var agentsMdDir = Directory.Exists(Path.Combine(dirPath, "agents"))
            ? Path.Combine(dirPath, "agents")
            : dirPath;
        foreach (var mdFile in Directory.GetFiles(agentsMdDir, "*.md", SearchOption.TopDirectoryOnly))
        {
            if (Path.GetFileName(mdFile).Equals("SKILL.md", StringComparison.OrdinalIgnoreCase))
                continue;
            try   { ImportAgentFromMd(mdFile, overwrite, result); }
            catch (Exception ex) { result.Errors.Add($"Agent '{Path.GetFileName(mdFile)}': {ex.Message}"); }
        }

        // Skills
        IEnumerable<string> skillDirs = Directory.Exists(Path.Combine(dirPath, "skills"))
            ? Directory.GetDirectories(Path.Combine(dirPath, "skills"))
            : Directory.GetDirectories(dirPath).Where(d => File.Exists(Path.Combine(d, "SKILL.md")));
        foreach (var skillSrcDir in skillDirs)
        {
            if (!File.Exists(Path.Combine(skillSrcDir, "SKILL.md"))) continue;
            try   { ImportSkillFromDirectory(skillSrcDir, overwrite, result); }
            catch (Exception ex) { result.Errors.Add($"Skill '{Path.GetFileName(skillSrcDir)}': {ex.Message}"); }
        }

        return result;
    }

    private void ImportAgentFromMd(string mdPath, bool overwrite, ImportDirectoryResult result)
    {
        var raw = File.ReadAllText(mdPath, new UTF8Encoding(false));
        string name = "", description = "";

        var match = Regex.Match(raw.TrimStart(),
            @"\A---\r?\n([\s\S]*?)\r?\n---\r?\n?", RegexOptions.CultureInvariant);
        if (match.Success)
        {
            foreach (var line in match.Groups[1].Value.Replace("\r\n", "\n").Split('\n'))
            {
                var idx = line.IndexOf(':');
                if (idx <= 0) continue;
                var key = line[..idx].Trim();
                var val = line[(idx + 1)..].Trim().Trim('"', '\'');
                if (key.Equals("name",        StringComparison.OrdinalIgnoreCase)) name        = val;
                if (key.Equals("description", StringComparison.OrdinalIgnoreCase)) description = val;
            }
        }
        if (string.IsNullOrWhiteSpace(name))
            name = Path.GetFileNameWithoutExtension(mdPath);

        var id = name.ToLowerInvariant()
                     .Replace(" ", "-").Replace("/", "-").Replace("\\", "-");

        if (!overwrite && File.Exists(Path.Combine(AgentsDir, id + ".json")))
        { result.AgentsSkipped++; return; }

        var now = DateTimeOffset.Now;
        var existingJsonPath = Path.Combine(AgentsDir, id + ".json");
        AgentDefinition? existing = null;
        if (File.Exists(existingJsonPath))
        {
            try { existing = JsonSerializer.Deserialize<AgentDefinition>(
                      File.ReadAllText(existingJsonPath, new UTF8Encoding(false)), JsonOptions); }
            catch { }
        }

        var def = new AgentDefinition
        {
            Id          = id,
            Name        = name,
            Description = description,
            ContentFile = id + ".md",
            CreatedAt   = existing?.CreatedAt ?? now,
            UpdatedAt   = now
        };

        File.WriteAllText(Path.Combine(AgentsDir, id + ".json"),
            JsonSerializer.Serialize(def, JsonOptions), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(AgentsDir, id + ".md"), raw, new UTF8Encoding(false));

        result.AgentsImported++;
    }

    private void ImportSkillFromDirectory(string skillSrcDir, bool overwrite, ImportDirectoryResult result)
    {
        var skillMdPath = Path.Combine(skillSrcDir, "SKILL.md");
        var raw = File.ReadAllText(skillMdPath, new UTF8Encoding(false));
        string name = "", description = "";

        var match = Regex.Match(raw.TrimStart(),
            @"\A---\r?\n([\s\S]*?)\r?\n---\r?\n?", RegexOptions.CultureInvariant);
        if (match.Success)
        {
            foreach (var line in match.Groups[1].Value.Replace("\r\n", "\n").Split('\n'))
            {
                var idx = line.IndexOf(':');
                if (idx <= 0) continue;
                var key = line[..idx].Trim();
                var val = line[(idx + 1)..].Trim().Trim('"', '\'');
                if (key.Equals("name",        StringComparison.OrdinalIgnoreCase)) name        = val;
                if (key.Equals("description", StringComparison.OrdinalIgnoreCase)) description = val;
            }
        }
        if (string.IsNullOrWhiteSpace(name))
            name = Path.GetFileName(skillSrcDir);

        var id = name.ToLowerInvariant()
                     .Replace(" ", "-").Replace("/", "-").Replace("\\", "-");

        if (Array.Exists(BuiltInSkillNames, n => n == id))
        {
            result.Errors.Add($"'{id}' is a built-in skill and cannot be overwritten.");
            return;
        }

        var dstSkillDir = Path.Combine(SkillsDir, id);
        if (!overwrite && Directory.Exists(dstSkillDir))
        { result.SkillsSkipped++; return; }

        CopyDirectoryContents(skillSrcDir, dstSkillDir);

        var metaPath = Path.Combine(dstSkillDir, "meta.json");
        SkillDefinition meta;
        if (File.Exists(metaPath))
        {
            try { meta = JsonSerializer.Deserialize<SkillDefinition>(
                      File.ReadAllText(metaPath, new UTF8Encoding(false)), JsonOptions)
                      ?? new SkillDefinition { CreatedAt = DateTimeOffset.Now }; }
            catch { meta = new SkillDefinition { CreatedAt = DateTimeOffset.Now }; }
        }
        else
        {
            meta = new SkillDefinition { CreatedAt = DateTimeOffset.Now };
        }

        if (string.IsNullOrWhiteSpace(meta.Name))        meta.Name        = name;
        if (string.IsNullOrWhiteSpace(meta.Description)) meta.Description = description;
        meta.Id               = id;
        meta.IsBuiltIn        = false;
        meta.ContentDirectory = dstSkillDir;
        meta.UpdatedAt        = DateTimeOffset.Now;

        File.WriteAllText(metaPath,
            JsonSerializer.Serialize(meta, JsonOptions), new UTF8Encoding(false));

        result.SkillsImported++;
    }


    private static string EmbedFrontmatter(AgentDefinition def, string content)
    {
        var trimmed = content.TrimStart();
        if (trimmed.StartsWith("---\n", StringComparison.Ordinal) ||
            trimmed.StartsWith("---\r\n", StringComparison.Ordinal))
            return content; // already has frontmatter

        var name = (string.IsNullOrWhiteSpace(def.Id) ? def.Name : def.Id)
            .Replace("\\", "\\\\").Replace("\"", "\\\"");
        var description = def.Description.Replace("\\", "\\\\").Replace("\"", "\\\"");
        var extraLines = (def.FrontmatterClaude ?? "")
            .Replace("\r\n", "\n").Split('\n')
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l) && l != "---")
            .ToList();
        var extraBlock = extraLines.Count > 0 ? "\n" + string.Join("\n", extraLines) : "";
        return $"---\nname: \"{name}\"\ndescription: \"{description}\"{extraBlock}\n---\n\n{trimmed}";
    }


    // ─── Helpers ──────────────────────────────────────────────────────────

    private static void EnsureDir(string dir)
    {
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AgentHubService] Delete failed: {path} - {ex.Message}");
        }
    }

    private static void AddDirectoryToZip(ZipArchive zip, string sourceDir, string zipPrefix)
    {
        if (!Directory.Exists(sourceDir))
            return;

        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, file).Replace("\\", "/");
            zip.CreateEntryFromFile(file, $"{zipPrefix}/{relative}");
        }
    }

    private static string? FindFirstExistingDirectory(params string[] candidates)
    {
        foreach (var dir in candidates)
        {
            if (Directory.Exists(dir))
                return dir;
        }
        return null;
    }

    private static void CopyDirectoryContents(string sourceDir, string targetDir)
    {
        EnsureDir(targetDir);
        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, file);
            var targetPath = Path.Combine(targetDir, relative);
            var targetParent = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(targetParent))
                EnsureDir(targetParent);
            File.Copy(file, targetPath, overwrite: true);
        }
    }

    private static void TryDeleteDirectory(string dirPath)
    {
        try
        {
            if (Directory.Exists(dirPath))
                Directory.Delete(dirPath, recursive: true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AgentHubService] Temp directory cleanup failed: {dirPath} - {ex.Message}");
        }
    }

    private string ResolveAgentHubConfigDir()
    {
        var settings = _configService.LoadSettings();
        if (!string.IsNullOrWhiteSpace(settings.CloudSyncRoot))
        {
            var cloudRoot = Environment.ExpandEnvironmentVariables(settings.CloudSyncRoot).Trim();
            return Path.Combine(cloudRoot, "_config");
        }

        return _configService.ConfigDir;
    }

    // ─── Embedded skill helpers ───────────────────────────────────────────

    private static List<(string RelativePath, string Content)> ReadEmbeddedSkillFiles(string skillName)
    {
        var manifest = ReadCclAssetText($"skills/{skillName}/MANIFEST");
        if (manifest != null)
        {
            var results = new List<(string, string)>();
            foreach (var line in manifest.Split('\n'))
            {
                var relativePath = line.Trim();
                if (string.IsNullOrEmpty(relativePath)) continue;
                var content = ReadCclAssetText($"skills/{skillName}/{relativePath}");
                if (content != null) results.Add((relativePath, content));
            }
            return results;
        }

        var skillContent = ReadCclAssetText($"skills/{skillName}/SKILL.md");
        if (skillContent != null)
            return [("SKILL.md", skillContent)];

        return [];
    }

    private static string? ReadCclAssetText(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/').TrimStart('/');
        var suffix = $"Assets.ContextCompressionLayer.{normalized.Replace('/', '.')}";
        var resourceNames = _assembly.GetManifestResourceNames();
        var resourceName = Array.Find(resourceNames,
            n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

        if (resourceName == null)
        {
            var normalizedSuffix = NormalizeResourceKey(suffix);
            resourceName = Array.Find(resourceNames,
                n => NormalizeResourceKey(n).EndsWith(normalizedSuffix, StringComparison.Ordinal));
        }

        if (resourceName == null) return null;

        using var stream = _assembly.GetManifestResourceStream(resourceName);
        if (stream == null) return null;
        using var reader = new StreamReader(stream, new UTF8Encoding(false));
        return reader.ReadToEnd();
    }

    private static string NormalizeResourceKey(string key)
        => key.Replace('-', '_').Replace(' ', '_').ToLowerInvariant();

    private static string SkillIdToDisplayName(string id)
    {
        var parts = id.Split('-');
        return string.Join(" ", parts.Select(p =>
            p.Length > 0 ? char.ToUpperInvariant(p[0]) + p[1..] : p));
    }
}
