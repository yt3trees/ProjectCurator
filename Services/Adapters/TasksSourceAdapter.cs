using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Curia.Models;

namespace Curia.Services.Adapters;

public class TasksSourceAdapter : ICuriaSourceAdapter
{
    private static readonly Regex TaskLineRegex = new(
        @"^- (?:\[[ x]\]|🔄|✅)\s+(?<title>.+)$",
        RegexOptions.Compiled);

    private static readonly Regex CleanTagRegex = new(
        @"\[(High|Medium|Low|Collab|id:[^\]]+)\]|\(Due:\s*\d{4}-\d{2}-\d{2}\)",
        RegexOptions.Compiled);

    public CuriaSourceType SourceType => CuriaSourceType.Tasks;

    public async Task<List<CuriaCandidateMeta>> EnumerateCandidatesAsync(
        IEnumerable<ProjectInfo> projects,
        DateTime since,
        CancellationToken ct)
    {
        var result = new List<CuriaCandidateMeta>();

        foreach (var proj in projects)
        {
            var tasksFile = Path.Combine(proj.AiContextPath, "obsidian_notes", "tasks.md");
            if (!File.Exists(tasksFile)) continue;

            var lastMod = File.GetLastWriteTime(tasksFile);
            if (lastMod < since) continue;

            try
            {
                var content = await File.ReadAllTextAsync(tasksFile, Encoding.UTF8, ct);
                var lines = content.Split('\n');
                string? currentSection = null;

                for (int i = 0; i < lines.Length; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var line = lines[i];

                    if (line.StartsWith("## ") || line.StartsWith("# "))
                    {
                        currentSection = line.TrimStart('#', ' ').Trim();
                        continue;
                    }

                    // top-level tasks only (no leading spaces)
                    if (!line.StartsWith("- ")) continue;
                    var match = TaskLineRegex.Match(line);
                    if (!match.Success) continue;

                    var rawTitle = match.Groups["title"].Value.Trim();
                    var cleanTitle = CleanTagRegex.Replace(rawTitle, "").Trim();

                    // collect description lines (blockquotes following this task)
                    var snippetParts = new List<string> { cleanTitle };
                    for (int j = i + 1; j < lines.Length && j < i + 5; j++)
                    {
                        var next = lines[j];
                        if (next.TrimStart().StartsWith("> "))
                            snippetParts.Add(next.Trim().TrimStart('>').Trim());
                        else if (string.IsNullOrWhiteSpace(next))
                            continue;
                        else
                            break;
                    }
                    if (currentSection != null)
                        snippetParts.Add($"[{currentSection}]");

                    result.Add(new CuriaCandidateMeta
                    {
                        Path = $"{tasksFile}#{i + 1}",
                        SourceType = CuriaSourceType.Tasks,
                        ProjectId = proj.Name,
                        Title = cleanTitle,
                        Snippet = string.Join(" / ", snippetParts.Where(s => !string.IsNullOrEmpty(s))),
                        LastModified = lastMod,
                    });
                }
            }
            catch { }
        }

        return result;
    }

    public async Task<string> ReadFullContentAsync(string path, CancellationToken ct)
    {
        var hashIdx = path.LastIndexOf('#');
        var filePath = hashIdx >= 0 ? path[..hashIdx] : path;
        int? lineHint = hashIdx >= 0 && int.TryParse(path[(hashIdx + 1)..], out var ln) ? ln : null;

        if (!File.Exists(filePath)) return "";

        var content = await File.ReadAllTextAsync(filePath, Encoding.UTF8, ct);
        if (lineHint == null)
            return content.Length > 200_000 ? content[..200_000] : content;

        var lines = content.Split('\n');
        int startIdx = lineHint.Value - 1;
        if (startIdx >= lines.Length) return "";

        var sb = new StringBuilder();
        sb.AppendLine(lines[startIdx]);
        for (int i = startIdx + 1; i < lines.Length; i++)
        {
            var l = lines[i];
            // Stop at next top-level task line
            if (l.StartsWith("- ") && TaskLineRegex.IsMatch(l)) break;
            sb.AppendLine(l);
        }

        return sb.ToString();
    }
}
