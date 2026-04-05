using System;

namespace Curia.Models;

public class CommandItem
{
    public string Label { get; set; } = "";
    public string Category { get; set; } = ""; // "tab", "project", "editor", etc.
    public string Display { get; set; } = "";
    public Action<MainWindow>? Action { get; set; }
}
