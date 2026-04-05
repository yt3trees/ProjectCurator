using System.Text.Json.Serialization;

namespace Curia.Models;

/// <summary>
/// プロジェクトディレクトリ内の asana_config.json に対応するモデル。
/// </summary>
public class AsanaProjectConfig
{
    [JsonPropertyName("asana_project_gids")]
    public List<string> AsanaProjectGids { get; set; } = [];

    [JsonPropertyName("anken_aliases")]
    public List<string> AnkenAliases { get; set; } = [];

    [JsonPropertyName("workstream_project_map")]
    public Dictionary<string, string> WorkstreamProjectMap { get; set; } = [];

    [JsonPropertyName("workstream_field_name")]
    public string WorkstreamFieldName { get; set; } = "workstream-id";

    [JsonPropertyName("team_view")]
    public TeamViewConfig? TeamView { get; set; }
}

/// <summary>
/// asana_config.json の team_view セクション。
/// </summary>
public class TeamViewConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    /// <summary>全 workstream に共通して適用する Asana Project GIDs。</summary>
    [JsonPropertyName("project_gids")]
    public List<string> ProjectGids { get; set; } = [];

    /// <summary>workstream 単位で適用する Asana Project GIDs。キーは workstream-id。</summary>
    [JsonPropertyName("workstream_project_gids")]
    public Dictionary<string, List<string>> WorkstreamProjectGids { get; set; } = [];
}
