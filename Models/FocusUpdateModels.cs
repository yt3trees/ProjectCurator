namespace ProjectCurator.Models;

public enum WorkMode { General, SharedWork }

public enum BackupStatus { Created, AlreadyExists }

public class FocusUpdateResult
{
    public string CurrentContent  { get; set; } = "";
    public string ProposedContent { get; set; } = "";
    public string BackupPath      { get; set; } = "";
    public BackupStatus BackupStatus { get; set; }
    public string TargetFocusPath { get; set; } = "";
    public WorkMode WorkMode      { get; set; }
    public string WorkstreamId    { get; set; } = "";
    public string Summary         { get; set; } = "";
    public string DebugSystemPrompt { get; set; } = "";
    public string DebugUserPrompt   { get; set; } = "";
    public string DebugResponse     { get; set; } = "";
}
