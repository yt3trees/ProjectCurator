namespace Curia.Models;

public enum SyncLogEntryKind
{
    Header,
    Separator,
    Step,
    Section,
    Found,
    Fetching,
    FetchResult,
    Output,
    Done,
    Error,
    Info,
    Skipped,
    Empty,
}

public class SyncLogEntry
{
    public SyncLogEntryKind Kind { get; set; }
    public string Text { get; set; } = "";
    public string? SubText { get; set; }
    public string? Badge { get; set; }
}
