namespace Curia.Models;

public enum AsanaTaskStatus
{
    InProgress,
    Completed,
    NotStarted
}

public enum AsanaTaskAssigneeType
{
    Owner,
    Collaborator
}

public class ParsedAsanaTask
{
    public string Title { get; set; } = "";
    public string? Id { get; set; }
    public AsanaTaskStatus Status { get; set; }
    public string Priority { get; set; } = "";   // "最高", "High", "Medium", "Low", ""
    public AsanaTaskAssigneeType AssigneeType { get; set; } = AsanaTaskAssigneeType.Owner;
    public string? DueDate { get; set; }
    public string RawLine { get; set; } = "";
    /// <summary>サブタスクの場合に設定される親タスクのタイトル。トップレベルタスクは null。</summary>
    public string? ParentTitle { get; set; }
    /// <summary>Asana notes の先頭数行 (tasks.md の &gt; blockquote 行から収集)。</summary>
    public string? Description { get; set; }
}

public class AsanaTaskParseResult
{
    public List<ParsedAsanaTask> InProgress    { get; set; } = [];
    public List<ParsedAsanaTask> Completed     { get; set; } = [];
    public List<ParsedAsanaTask> NotStarted    { get; set; } = [];
    public List<ParsedAsanaTask> Collaborating { get; set; } = [];
    public string SourcePath { get; set; } = "";
}
