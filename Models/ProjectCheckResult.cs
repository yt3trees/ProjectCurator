using System;
using System.Collections.Generic;
using System.Linq;

namespace Curia.Models;

public class ProjectCheckResult
{
    public string ProjectName { get; set; } = "";
    public string ProjectPath { get; set; } = "";
    public List<CheckItem> Items { get; } = [];
    public bool HasError => Items.Any(i => i.Status == "Error");
    public bool HasWarning => Items.Any(i => i.Status == "Warning");
}

public class CheckItem
{
    public string Name { get; set; } = "";
    public string Status { get; set; } = "OK"; // "OK", "Warning", "Error", "Info"
    public string Message { get; set; } = "";
}

public class ProjectArchiveResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public List<string> Logs { get; } = [];
}

public class ProjectConvertResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public List<string> Logs { get; } = [];
}

public class ProjectSetupResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public List<string> Logs { get; } = [];
}
