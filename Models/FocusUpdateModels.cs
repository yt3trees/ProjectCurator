namespace ProjectCurator.Models;

public enum WorkMode { General, SharedWork }

public enum BackupStatus { Created, AlreadyExists }

public class FocusUpdateResult : FileUpdateProposal
{
    public string BackupPath      { get; set; } = "";
    public BackupStatus BackupStatus { get; set; }
    public string TargetFocusPath { get; set; } = "";
    public WorkMode WorkMode      { get; set; }
    public string WorkstreamId    { get; set; } = "";
}
