using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ProjectCurator.Helpers;
using ProjectCurator.Models;

namespace ProjectCurator.Services;

public class ConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private static readonly JsonSerializerOptions JsonOptionsWithEnum = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string ConfigDir { get; }

    public ConfigService()
    {
        ConfigDir = DetectConfigDir();
    }

    public ConfigService(string configDir)
    {
        ConfigDir = configDir;
    }

    private static string DetectConfigDir()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // 1. 環境変数オーバーライド
        var envOverride = Environment.GetEnvironmentVariable("PROJECTCURATOR_CONFIG_DIR");
        if (!string.IsNullOrWhiteSpace(envOverride) && Directory.Exists(envOverride))
            return envOverride;

        // 2. 新標準パス
        var newPath = Path.Combine(userProfile, ".projectcurator");
        if (File.Exists(Path.Combine(newPath, "settings.json")))
            return newPath;

        // 3. 後方互換 (旧パス)
        var legacyPath = Path.Combine(userProfile, "Documents", "Projects", "_config");
        if (File.Exists(Path.Combine(legacyPath, "settings.json")))
            return legacyPath;

        // 4. 初回起動デフォルト
        return newPath;
    }

    // ---------- AppSettings ----------

    public AppSettings LoadSettings()
    {
        var settings = LoadSettingsCore();

        // EnvironmentVariables 展開
        settings.LocalProjectsRoot = Environment.ExpandEnvironmentVariables(settings.LocalProjectsRoot);
        settings.CloudSyncRoot = Environment.ExpandEnvironmentVariables(settings.CloudSyncRoot);
        settings.ObsidianVaultRoot = Environment.ExpandEnvironmentVariables(settings.ObsidianVaultRoot);

        // デフォルト補正
        settings.Hotkey ??= new HotkeyConfig();
        settings.CaptureHotkey ??= new HotkeyConfig { Modifiers = "Ctrl+Shift", Key = "C" };
        settings.AsanaSync ??= new AsanaSyncConfig();
        return settings;
    }

    public void SaveSettings(AppSettings settings)
    {
        EnsureConfigDir();
        var path = Path.Combine(ConfigDir, "settings.json");
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    // ---------- AsanaGlobalConfig ----------

    public AsanaGlobalConfig LoadAsanaGlobalConfig()
    {
        var path = Path.Combine(ConfigDir, "asana_global.json");
        if (!File.Exists(path))
            return new AsanaGlobalConfig();

        var (content, _) = EncodingDetector.ReadFile(path);
        return JsonSerializer.Deserialize<AsanaGlobalConfig>(content, JsonOptions) ?? new AsanaGlobalConfig();
    }

    public void SaveAsanaGlobalConfig(AsanaGlobalConfig config)
    {
        EnsureConfigDir();
        var path = Path.Combine(ConfigDir, "asana_global.json");
        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    // ---------- HiddenProjects ----------

    public List<string> LoadHiddenProjects()
    {
        var path = Path.Combine(ConfigDir, "hidden_projects.json");
        if (!File.Exists(path))
            return [];

        var content = File.ReadAllText(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return JsonSerializer.Deserialize<List<string>>(content, JsonOptions) ?? [];
    }

    public void SaveHiddenProjects(List<string> hidden)
    {
        EnsureConfigDir();
        var path = Path.Combine(ConfigDir, "hidden_projects.json");
        var json = JsonSerializer.Serialize(hidden, JsonOptions);
        File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    // ---------- PinnedFolders ----------

    public List<PinnedFolder> LoadPinnedFolders()
    {
        var path = Path.Combine(ConfigDir, "pinned_folders.json");
        if (!File.Exists(path))
            return [];

        try
        {
            var content = File.ReadAllText(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return JsonSerializer.Deserialize<List<PinnedFolder>>(content, JsonOptions) ?? [];
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ConfigService] LoadPinnedFolders error: {ex}");
            return [];
        }
    }

    public void SavePinnedFolders(List<PinnedFolder> pinned)
    {
        EnsureConfigDir();
        var path = Path.Combine(ConfigDir, "pinned_folders.json");
        var json = JsonSerializer.Serialize(pinned, JsonOptions);
        File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    // ---------- WindowPlacement ----------

    public WindowPlacement? LoadWindowPlacement()
    {
        var path = Path.Combine(ConfigDir, "window_state.json");
        if (!File.Exists(path))
            return null;
        try
        {
            var content = File.ReadAllText(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return JsonSerializer.Deserialize<WindowPlacement>(content, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public void SaveWindowPlacement(WindowPlacement placement)
    {
        EnsureConfigDir();
        var path = Path.Combine(ConfigDir, "window_state.json");
        var json = JsonSerializer.Serialize(placement, JsonOptions);
        File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    // ---------- AgentHubState ----------

    public AgentHubState LoadAgentHubState()
    {
        var path = Path.Combine(ConfigDir, "agent_hub_state.json");
        if (!File.Exists(path))
            return new AgentHubState();
        try
        {
            var content = File.ReadAllText(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return JsonSerializer.Deserialize<AgentHubState>(content, JsonOptionsWithEnum) ?? new AgentHubState();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ConfigService] LoadAgentHubState error: {ex}");
            return new AgentHubState();
        }
    }

    public void SaveAgentHubState(AgentHubState state)
    {
        EnsureConfigDir();
        var path = Path.Combine(ConfigDir, "agent_hub_state.json");
        var json = JsonSerializer.Serialize(state, JsonOptionsWithEnum);
        File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    // ---------- GlobalAgentHubProfiles ----------

    public List<GlobalDeploymentProfile> LoadGlobalAgentHubProfiles()
    {
        var path = Path.Combine(ConfigDir, "global_agent_hub_profiles.json");
        if (!File.Exists(path))
        {
            var defaults = CreateDefaultGlobalProfiles();
            SaveGlobalAgentHubProfiles(defaults);
            return defaults;
        }

        try
        {
            var content = File.ReadAllText(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            var parsed = JsonSerializer.Deserialize<GlobalDeploymentProfilesConfig>(content, JsonOptionsWithEnum);
            var profiles = parsed?.Profiles ?? [];
            if (profiles.Count == 0)
            {
                profiles = CreateDefaultGlobalProfiles();
                SaveGlobalAgentHubProfiles(profiles);
            }

            ExpandGlobalProfilePaths(profiles);
            return profiles;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ConfigService] LoadGlobalAgentHubProfiles error: {ex}");
            var fallback = CreateDefaultGlobalProfiles();
            ExpandGlobalProfilePaths(fallback);
            return fallback;
        }
    }

    public void SaveGlobalAgentHubProfiles(List<GlobalDeploymentProfile> profiles)
    {
        EnsureConfigDir();
        var normalized = profiles
            .Where(p => p != null)
            .Select(p =>
            {
                p.UpdatedAt = DateTimeOffset.Now;
                return p;
            })
            .ToList();
        var payload = new GlobalDeploymentProfilesConfig
        {
            Profiles = normalized
        };
        var path = Path.Combine(ConfigDir, "global_agent_hub_profiles.json");
        var json = JsonSerializer.Serialize(payload, JsonOptionsWithEnum);
        File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    // ---------- AsanaProjectConfig (per project) ----------

    public Models.AsanaProjectConfig? LoadAsanaProjectConfig(Models.ProjectInfo project)
    {
        var configPath = ResolveAsanaConfigPath(project);
        if (!File.Exists(configPath)) return null;
        try
        {
            var (content, _) = Helpers.EncodingDetector.ReadFile(configPath);
            return JsonSerializer.Deserialize<Models.AsanaProjectConfig>(content, JsonOptions);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ConfigService] LoadAsanaProjectConfig error: {ex}");
            return null;
        }
    }

    public void SaveAsanaProjectConfig(Models.ProjectInfo project, Models.AsanaProjectConfig config)
    {
        var configPath = ResolveAsanaConfigPath(project);
        var dir = Path.GetDirectoryName(configPath)!;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(configPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public string ResolveAsanaConfigPath(Models.ProjectInfo project)
    {
        var settings = LoadSettings();
        var syncRoot = settings.CloudSyncRoot.TrimEnd('\\', '/');
        return project.Category == "domain"
            ? project.Tier == "mini"
                ? Path.Combine(syncRoot, "_domains", "_mini", project.Name, "asana_config.json")
                : Path.Combine(syncRoot, "_domains", project.Name, "asana_config.json")
            : project.Tier == "mini"
                ? Path.Combine(syncRoot, "_mini", project.Name, "asana_config.json")
                : Path.Combine(syncRoot, project.Name, "asana_config.json");
    }

    public string GetObsidianProjectPath(Models.ProjectInfo project)
    {
        var settings = LoadSettings();
        var obsRoot = settings.ObsidianVaultRoot.TrimEnd('\\', '/');
        var relPath = GetObsidianRelativePath(project);
        return Path.Combine(obsRoot, relPath);
    }

    public static string GetObsidianRelativePath(Models.ProjectInfo project)
    {
        if (project.Name == "_INHOUSE") return "_INHOUSE";
        return (project.Category, project.Tier) switch
        {
            ("domain", "mini") => Path.Combine("Projects", "_domains", "_mini", project.Name),
            ("domain", _)      => Path.Combine("Projects", "_domains", project.Name),
            (_, "mini")        => Path.Combine("Projects", "_mini", project.Name),
            _                  => Path.Combine("Projects", project.Name)
        };
    }

    // ---------- private ----------

    private void EnsureConfigDir()
    {
        if (!Directory.Exists(ConfigDir))
            Directory.CreateDirectory(ConfigDir);
    }

    private AppSettings LoadSettingsCore()
    {
        var path = Path.Combine(ConfigDir, "settings.json");
        if (!File.Exists(path))
            return new AppSettings();

        try
        {
            var content = File.ReadAllText(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return JsonSerializer.Deserialize<AppSettings>(content, JsonOptions) ?? new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
    }

    private static List<GlobalDeploymentProfile> CreateDefaultGlobalProfiles()
    {
        return
        [
            new GlobalDeploymentProfile
            {
                Id = "personal",
                Name = "Personal",
                ClaudeBasePath = "%USERPROFILE%\\.claude",
                CodexBasePath = "%USERPROFILE%\\.codex",
                CopilotBasePath = "%USERPROFILE%",
                GeminiBasePath = "%USERPROFILE%\\.gemini",
                ClaudeRuleFilePath = "%USERPROFILE%\\CLAUDE.md",
                CodexRuleFilePath = "%USERPROFILE%\\AGENTS.md",
                CopilotRuleFilePath = "%USERPROFILE%\\.github\\copilot-instructions.md",
                GeminiRuleFilePath = "%USERPROFILE%\\GEMINI.md",
                UpdatedAt = DateTimeOffset.Now
            }
        ];
    }

    private static void ExpandGlobalProfilePaths(IEnumerable<GlobalDeploymentProfile> profiles)
    {
        foreach (var profile in profiles)
        {
            profile.ClaudeBasePath = Environment.ExpandEnvironmentVariables(profile.ClaudeBasePath ?? "");
            profile.CodexBasePath = Environment.ExpandEnvironmentVariables(profile.CodexBasePath ?? "");
            profile.CopilotBasePath = Environment.ExpandEnvironmentVariables(profile.CopilotBasePath ?? "");
            profile.GeminiBasePath = Environment.ExpandEnvironmentVariables(profile.GeminiBasePath ?? "");
            profile.ClaudeRuleFilePath = Environment.ExpandEnvironmentVariables(profile.ClaudeRuleFilePath ?? "");
            profile.CodexRuleFilePath = Environment.ExpandEnvironmentVariables(profile.CodexRuleFilePath ?? "");
            profile.CopilotRuleFilePath = Environment.ExpandEnvironmentVariables(profile.CopilotRuleFilePath ?? "");
            profile.GeminiRuleFilePath = Environment.ExpandEnvironmentVariables(profile.GeminiRuleFilePath ?? "");
        }
    }
}
