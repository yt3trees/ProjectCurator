namespace Curia.Models;

public class PinnedFolder
{
    public string Project { get; set; } = "";
    public string? Workstream { get; set; }
    public string Folder { get; set; } = "";
    public string FullPath { get; set; } = "";
    public string PinnedAt { get; set; } = "";

    public bool FolderExists => System.IO.Directory.Exists(FullPath);

    // "ProjectA / core-feature" or "ProjectA"
    public string ProjectLabel => Workstream is null ? Project : $"{Project} / {Workstream}";
}
