namespace Curia.Models;

public enum SilenceSeverity { Low, Medium, High }

public class SilenceAlert
{
    public string ProjectId { get; set; } = "";
    public string ProjectDisplayName { get; set; } = "";
    public SilenceSeverity Severity { get; set; }
    public string Reason { get; set; } = "";
    public DateTime DetectedAt { get; set; }
}

public class SilenceAlertState
{
    public DateTime LastRunAt { get; set; }
    public List<SilenceAlert> Alerts { get; set; } = [];
    public Dictionary<string, DateTime> DismissedUntil { get; set; } = [];
    public Dictionary<string, DateTime> SnoozedUntil { get; set; } = [];
}
