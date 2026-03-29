using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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

    private readonly ConfigService _configService;

    public string AgentHubDir => Path.Combine(ResolveAgentHubConfigDir(), "agent_hub");
    public string AgentsDir => Path.Combine(AgentHubDir, "agents");
    public string RulesDir => Path.Combine(AgentHubDir, "rules");

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
        File.WriteAllText(mdPath, content, new UTF8Encoding(false));
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
    }

    public void ImportLibraryZip(string sourceZipPath)
    {
        if (!File.Exists(sourceZipPath))
            throw new FileNotFoundException("ZIP file not found.", sourceZipPath);

        EnsureDir(AgentHubDir);
        EnsureDir(AgentsDir);
        EnsureDir(RulesDir);

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

            if (extractedAgentsDir == null && extractedRulesDir == null)
                throw new InvalidDataException("Invalid library ZIP. Expected 'agents/' or 'rules/' folder.");

            if (extractedAgentsDir != null)
                CopyDirectoryContents(extractedAgentsDir, AgentsDir);
            if (extractedRulesDir != null)
                CopyDirectoryContents(extractedRulesDir, RulesDir);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    public AgentDefinition ImportAgentFromMarkdown(string sourceMarkdownPath)
    {
        var name = Path.GetFileNameWithoutExtension(sourceMarkdownPath);
        var content = File.ReadAllText(sourceMarkdownPath, new UTF8Encoding(false));
        var def = new AgentDefinition
        {
            Name = name,
            Description = $"Imported from {Path.GetFileName(sourceMarkdownPath)}"
        };
        SaveAgentDefinition(def, content);
        return def;
    }

    public ContextRuleDefinition ImportRuleFromMarkdown(string sourceMarkdownPath)
    {
        var name = Path.GetFileNameWithoutExtension(sourceMarkdownPath);
        var content = File.ReadAllText(sourceMarkdownPath, new UTF8Encoding(false));
        var def = new ContextRuleDefinition
        {
            Name = name,
            Description = $"Imported from {Path.GetFileName(sourceMarkdownPath)}"
        };
        SaveRuleDefinition(def, content);
        return def;
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
}
