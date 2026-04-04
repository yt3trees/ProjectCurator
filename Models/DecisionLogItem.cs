namespace ProjectCurator.Models;

public class DecisionLogItem
{
    public string Title { get; set; } = "";
    public string Date { get; set; } = "Unknown";
    public string Status { get; set; } = "";
    public string Trigger { get; set; } = "";
    public string ChosenSummary { get; set; } = "";
    public string WhySummary { get; set; } = "";
    public string FilePath { get; set; } = "";
}
