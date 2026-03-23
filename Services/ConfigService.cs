using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
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

    public string WorkspaceRoot { get; }
    public string ConfigDir => Path.Combine(WorkspaceRoot, "_config");

    public ConfigService()
    {
        WorkspaceRoot = DetectWorkspaceRoot();
    }

    public ConfigService(string workspaceRoot)
    {
        WorkspaceRoot = workspaceRoot;
    }

    private static string DetectWorkspaceRoot()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidate = Path.Combine(userProfile, "Documents", "Projects", "_config", "settings.json");
        if (File.Exists(candidate))
        {
            // WorkspaceRoot は _config の親
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(candidate)!, ".."));
        }

        // 見つからなければデフォルト
        return Path.Combine(userProfile, "Documents", "Projects");
    }

    // ---------- AppSettings ----------

    public AppSettings LoadSettings()
    {
        var settings = LoadSettingsCore();

        // EnvironmentVariables 展開
        settings.LocalProjectsRoot = Environment.ExpandEnvironmentVariables(settings.LocalProjectsRoot);
        settings.BoxProjectsRoot = Environment.ExpandEnvironmentVariables(settings.BoxProjectsRoot);
        settings.ObsidianVaultRoot = Environment.ExpandEnvironmentVariables(settings.ObsidianVaultRoot);

        // デフォルト補正
        settings.Hotkey ??= new HotkeyConfig();
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
}
