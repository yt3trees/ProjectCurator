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
    public bool SkipHiddenProjects { get; set; } = true;
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
/// window_state.json に対応する設定クラス。
/// </summary>
public class WindowPlacement
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
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
    public string CloudSyncRoot { get; set; } = "";
    public string ObsidianVaultRoot { get; set; } = "";
    public HotkeyConfig? Hotkey { get; set; }
    public HotkeyConfig? CaptureHotkey { get; set; }
    public AsanaSyncConfig? AsanaSync { get; set; }

    // LLM API 設定 (Update Focus from Asana 機能用)
    public string LlmProvider { get; set; } = "openai";         // "openai" | "azure_openai"
    public string LlmApiKey { get; set; } = "";
    public string LlmModel { get; set; } = "gpt-4o";
    public string LlmEndpoint { get; set; } = "";              // Azure OpenAI のエンドポイント URL
    public string LlmApiVersion { get; set; } = "2024-12-01-preview"; // Azure OpenAI の API バージョン
    public bool AiEnabled { get; set; } = false;               // AI 機能を有効にするか
    // 追加パラメータ (reasoning_effort, temperature, max_tokens 等)
    public Dictionary<string, string> LlmParameters { get; set; } = [];
    public string LlmUserProfile { get; set; } = "";
    // Capture 設定
    public bool CaptureTaskLogEnabled { get; set; } = false;   // task 起票成功時に asana-tasks.md へ即時追記するか
}
