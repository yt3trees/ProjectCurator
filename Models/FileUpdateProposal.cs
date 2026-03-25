namespace ProjectCurator.Models;

/// <summary>
/// LLM が生成したファイル更新提案。FocusUpdateResult および Tensions 更新で共用する。
/// </summary>
public class FileUpdateProposal
{
    public string CurrentContent    { get; set; } = "";
    public string ProposedContent   { get; set; } = "";
    public string Summary           { get; set; } = "";
    public string DebugSystemPrompt { get; set; } = "";
    public string DebugUserPrompt   { get; set; } = "";
    public string DebugResponse     { get; set; } = "";
}
