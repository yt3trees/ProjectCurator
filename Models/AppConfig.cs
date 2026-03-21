using System.Text.Json.Serialization;

namespace ProjectCurator.Models;

public class HotkeyConfig
{
    public string Modifiers { get; set; } = "Ctrl+Shift";
    public string Key { get; set; } = "P";
}

public class AsanaSyncConfig
{
    public bool Enabled { get; set; } = false;
    public int IntervalMin { get; set; } = 60;
}

/// <summary>
/// _config/asana_global.json に対応する設定クラス。
/// Asana API 認証情報とグローバル設定。
/// </summary>
public class AsanaGlobalConfig
{
    [JsonPropertyName("asana_token")]
    public string AsanaToken { get; set; } = "";

    [JsonPropertyName("workspace_gid")]
    public string WorkspaceGid { get; set; } = "";

    [JsonPropertyName("user_gid")]
    public string UserGid { get; set; } = "";

    [JsonPropertyName("personal_project_gids")]
    public List<string> PersonalProjectGids { get; set; } = [];

    [JsonPropertyName("output_file")]
    public string OutputFile { get; set; } = "";
}

/// <summary>
/// settings.json に対応する設定クラス。
/// </summary>
public class AppSettings
{
    public bool DashboardTodayQueueVisible { get; set; } = true;
    public int DashboardTodayQueueLimit { get; set; } = 5;
    public int DashboardAutoRefreshMinutes { get; set; } = 0;
    public string LocalProjectsRoot { get; set; } = "";
    public string BoxProjectsRoot { get; set; } = "";
    public string ObsidianVaultRoot { get; set; } = "";
    public HotkeyConfig? Hotkey { get; set; }
    public AsanaSyncConfig? AsanaSync { get; set; }
}
