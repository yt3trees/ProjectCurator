namespace ProjectCurator.Models;

public class CommandItem
{
    public string Label { get; set; } = "";
    public string Category { get; set; } = ""; // "tab", "project", "editor", etc.
    public string Display { get; set; } = "";
    // Platform-agnostic: Action parameter is the platform-specific window/host object.
    public Action<object>? Action { get; set; }
}
