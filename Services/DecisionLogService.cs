using System.IO;
using ProjectCurator.Models;

namespace ProjectCurator.Services;

public class DecisionLogService
{
    public async Task<List<DecisionLogItem>> GetDecisionLogsAsync(string projectAiContextPath, string? workstreamId = null)
    {
        var targetDir = string.IsNullOrWhiteSpace(workstreamId)
            ? Path.Combine(projectAiContextPath, "decision_log")
            : Path.Combine(projectAiContextPath, "workstreams", workstreamId, "decision_log");

        return await ParseDecisionLogsDirAsync(targetDir);
    }

    private async Task<List<DecisionLogItem>> ParseDecisionLogsDirAsync(string decisionLogDir)
    {
        var items = new List<DecisionLogItem>();

        if (!Directory.Exists(decisionLogDir))
            return items;

        var files = Directory.GetFiles(decisionLogDir, "*.md");
        foreach (var file in files)
        {
            try
            {
                var content = await File.ReadAllTextAsync(file);
                items.Add(ParseLogContent(content, file));
            }
            catch
            {
                // Skip unreadable files
            }
        }

        return items.OrderByDescending(i => 
        {
            if (DateTime.TryParse(i.Date, out var dt)) return dt;
            return DateTime.MinValue;
        }).ToList();
    }

    private DecisionLogItem ParseLogContent(string content, string filePath)
    {
        var item = new DecisionLogItem { FilePath = filePath };
        var lines = content.Split('\n');
        
        string? currentSection = null;
        var chosenLines = new List<string>();
        var whyLines = new List<string>();
        bool hasWhy = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');
            
            if (line.StartsWith("# "))
            {
                if (line.StartsWith("# Decision:"))
                    item.Title = line.Substring(11).Trim();
                currentSection = null;
                continue;
            }
            
            if (line.StartsWith("## "))
            {
                var lowerLine = line.ToLowerInvariant();
                if (lowerLine.StartsWith("## chosen"))
                {
                    currentSection = "chosen";
                }
                else if (lowerLine.StartsWith("## why") || lowerLine.StartsWith("## context"))
                {
                    if (lowerLine.StartsWith("## why"))
                    {
                        whyLines.Clear();
                        hasWhy = true;
                    }
                    else if (hasWhy)
                    {
                        // If we already parsed 'Why', we ignore 'Context' coming later
                        currentSection = null;
                        continue;
                    }
                    currentSection = "why";
                }
                else
                {
                    currentSection = null;
                }
                continue;
            }

            if (line.StartsWith("> Date:"))
            {
                item.Date = line.Substring(7).Trim();
            }
            else if (line.StartsWith("> Status:"))
            {
                item.Status = line.Substring(9).Trim();
            }
            else if (line.StartsWith("> Trigger:"))
            {
                item.Trigger = line.Substring(10).Trim();
            }
            else if (currentSection == "chosen")
            {
                if (!string.IsNullOrWhiteSpace(line) || chosenLines.Count > 0)
                    chosenLines.Add(line);
            }
            else if (currentSection == "why")
            {
                if (!string.IsNullOrWhiteSpace(line) || whyLines.Count > 0)
                    whyLines.Add(line);
            }
        }

        item.ChosenSummary = CleanSummary(string.Join(" ", chosenLines.Select(l => l.Trim()).Where(l => !string.IsNullOrEmpty(l))));
        item.WhySummary = CleanSummary(string.Join(" ", whyLines.Select(l => l.Trim()).Where(l => !string.IsNullOrEmpty(l))));

        if (string.IsNullOrEmpty(item.Title))
            item.Title = Path.GetFileNameWithoutExtension(filePath);

        return item;
    }

    private static string CleanSummary(string text)
    {
        if (text.Length > 150)
            return text.Substring(0, 150).Trim() + "...";
        return text.Trim();
    }
}
