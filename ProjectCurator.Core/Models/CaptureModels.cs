using System.Text.Json.Serialization;

namespace ProjectCurator.Models;

public class CaptureClassification
{
    [JsonPropertyName("category")]
    public string Category { get; set; } = "memo";   // "task" | "tension" | "focus_update" | "decision" | "memo"

    [JsonPropertyName("project")]
    public string ProjectName { get; set; } = "";

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = "";

    [JsonPropertyName("body")]
    public string Body { get; set; } = "";

    [JsonPropertyName("workstream_hint")]
    public string WorkstreamHint { get; set; } = "";

    [JsonPropertyName("project_candidate_gid")]
    public string AsanaProjectCandidateGid { get; set; } = "";

    [JsonPropertyName("section_candidate_gid")]
    public string AsanaSectionCandidateGid { get; set; } = "";

    [JsonPropertyName("due_on")]
    public string DueOn { get; set; } = "";   // "YYYY-MM-DD" or ""

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; } = 0.5;

    [JsonPropertyName("reasoning")]
    public string Reasoning { get; set; } = "";
}

public class CaptureRouteResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public string? TargetFilePath { get; set; }
    public string? AsanaTaskGid { get; set; }
    public string? AsanaTaskUrl { get; set; }
    public string? AsanaProjectGid { get; set; }
    public string? AsanaSectionGid { get; set; }
    public bool RequiresNavigation { get; set; }
    public string? NavigationProjectName { get; set; }
    public string? NavigationFilePath { get; set; }
}

/// <summary>Asana task 起票前の承認プレビュー</summary>
public class AsanaTaskCreatePreview
{
    public string ProjectName { get; set; } = "";
    public string ProjectGid { get; set; } = "";
    public string SectionName { get; set; } = "";
    public string SectionGid { get; set; } = "";
    public string TaskName { get; set; } = "";
    public string Notes { get; set; } = "";
    public string DueOn { get; set; } = "";
    public string DueAt { get; set; } = "";   // ISO 8601 with timezone, e.g. "2024-01-15T09:00:00.000+09:00"
    public string RequestJson { get; set; } = "";   // 実際に送信する JSON (Authorization 除く)
}

/// <summary>Asana project / section メタデータ (UI 表示用)</summary>
public class AsanaProjectMeta
{
    public string Gid { get; set; } = "";
    public string Name { get; set; } = "";
    public string DisplayLabel => string.IsNullOrWhiteSpace(Name) ? Gid : $"{Name} ({Gid})";
}

public class AsanaSectionMeta
{
    public string Gid { get; set; } = "";
    public string Name { get; set; } = "";
    public string DisplayLabel => string.IsNullOrWhiteSpace(Name) ? Gid : $"{Name} ({Gid})";
}
