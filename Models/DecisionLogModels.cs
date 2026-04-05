namespace Curia.Models;

public class DecisionLogDraftResult
{
    public string DraftContent      { get; set; } = "";
    public string SuggestedFileName { get; set; } = "";
    public string? ResolvedTension  { get; set; }
    public string DebugSystemPrompt { get; set; } = "";
    public string DebugUserPrompt   { get; set; } = "";
    public string DebugResponse     { get; set; } = "";
}

public class DetectedDecision
{
    public string Summary  { get; set; } = "";
    public string Evidence { get; set; } = "";
    public string Status   { get; set; } = "confirmed"; // "confirmed" | "tentative"
    public bool   IsSelected { get; set; } = true;
}

public class AiDecisionLogInputResult
{
    public bool   UseBlankTemplate    { get; set; }
    public string UserInput           { get; set; } = "";
    public string Status              { get; set; } = "Confirmed";
    public string Trigger             { get; set; } = "Solo decision";
    public List<DetectedDecision> SelectedCandidates { get; set; } = [];
    public List<string> AttachedFilePaths { get; set; } = [];
}
