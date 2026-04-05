namespace Curia.Models;

public class UncommittedRepoStatus
{
    public string RelativePath { get; set; } = ".";
    public string FullPath { get; set; } = "";
    public int StagedCount { get; set; }
    public int ModifiedCount { get; set; }
    public int UntrackedCount { get; set; }
    public int ConflictCount { get; set; }
    public List<string> PorcelainLines { get; set; } = [];

    public int TotalCount => PorcelainLines.Count;

    public string SummaryText
    {
        get
        {
            var parts = new List<string> { $"{TotalCount} changes" };
            if (StagedCount > 0) parts.Add($"staged {StagedCount}");
            if (ModifiedCount > 0) parts.Add($"modified {ModifiedCount}");
            if (UntrackedCount > 0) parts.Add($"untracked {UntrackedCount}");
            if (ConflictCount > 0) parts.Add($"conflicts {ConflictCount}");
            return string.Join(" | ", parts);
        }
    }
}

public class ProjectInfo
{
    public string Name { get; set; } = "";
    public string Tier { get; set; } = "full";  // "full" | "mini"
    public string Category { get; set; } = "project";  // "project" | "domain"
    public string Path { get; set; } = "";
    public string AiContextPath { get; set; } = "";
    public string AiContextContentPath { get; set; } = "";
    public string JunctionShared { get; set; } = "Missing";   // "OK"|"Missing"|"Broken"
    public string JunctionObsidian { get; set; } = "Missing";
    public string JunctionContext { get; set; } = "Missing";
    public string? FocusFile { get; set; }
    public string? SummaryFile { get; set; }
    public string? FileMapFile { get; set; }
    public string? AgentsFile { get; set; }
    public string? ClaudeFile { get; set; }
    public int? FocusLines { get; set; }
    public int? SummaryLines { get; set; }
    public int? FocusTokens { get; set; }
    public int? SummaryTokens { get; set; }
    public int? FocusAge { get; set; }
    public int? SummaryAge { get; set; }
    public int DecisionLogCount { get; set; }
    public List<DateTime> FocusHistoryDates { get; set; } = [];
    public List<DateTime> DecisionLogDates { get; set; } = [];
    public List<string> ExternalSharedPaths { get; set; } = [];
    public List<WorkstreamInfo> Workstreams { get; set; } = [];
    public bool HasUncommittedChanges { get; set; }
    public List<string> UncommittedRepoPaths { get; set; } = [];
    public List<UncommittedRepoStatus> UncommittedRepoStatuses { get; set; } = [];
    public bool HasWorkstreams => Workstreams.Count > 0;

    // Display helper
    public string DisplayName => Category == "domain"
        ? (Tier == "mini" ? $"{Name} [Domain][Mini]" : $"{Name} [Domain]")
        : (Tier == "mini" ? $"{Name} [Mini]" : Name);

    public string HiddenKey => $"{Name}|{Tier}|{Category}";

    public override string ToString() => DisplayName;
}
