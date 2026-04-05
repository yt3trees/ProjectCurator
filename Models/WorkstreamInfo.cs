namespace Curia.Models;

public class WorkstreamInfo
{
    public string Id { get; set; } = "";          // ディレクトリ名 (e.g. "infra-team-ops")
    public string Label { get; set; } = "";       // 表示名 (workstream.json があれば優先、なければ Id をそのまま使用)
    public string Path { get; set; } = "";        // フルパス: {AiContextContentPath}/workstreams/{Id}
    public bool IsClosed { get; set; }            // _closed ファイルが存在するか
    public string? FocusFile { get; set; }
    public int? FocusAge { get; set; }
    public int? FocusTokens { get; set; }
    public int DecisionLogCount { get; set; }
    public List<DateTime> FocusHistoryDates { get; set; } = [];
    public List<DateTime> DecisionLogDates { get; set; } = [];
}
