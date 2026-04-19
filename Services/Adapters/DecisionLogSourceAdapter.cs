using System.IO;
using System.Text;
using Curia.Models;

namespace Curia.Services.Adapters;

public class DecisionLogSourceAdapter : ICuriaSourceAdapter
{
    public CuriaSourceType SourceType => CuriaSourceType.DecisionLog;

    public async Task<List<CuriaCandidateMeta>> EnumerateCandidatesAsync(
        IEnumerable<ProjectInfo> projects,
        DateTime since,
        CancellationToken ct)
    {
        var result = new List<CuriaCandidateMeta>();

        foreach (var proj in projects)
        {
            var logDir = Path.Combine(proj.AiContextPath, "decision_log");
            if (!Directory.Exists(logDir)) continue;

            foreach (var file in Directory.GetFiles(logDir, "*.md", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                var lastMod = File.GetLastWriteTime(file);
                if (lastMod < since) continue;

                try
                {
                    var content = await File.ReadAllTextAsync(file, Encoding.UTF8, ct);
                    var titleLine = content.Split('\n')
                        .FirstOrDefault(l => l.StartsWith("# Decision:", StringComparison.OrdinalIgnoreCase))
                        ?? content.Split('\n').FirstOrDefault(l => l.StartsWith("#"))
                        ?? "";
                    var title = titleLine.TrimStart('#', ' ').Trim();
                    if (string.IsNullOrEmpty(title))
                        title = Path.GetFileNameWithoutExtension(file);

                    var snippet = content.Length > 500 ? content[..500] : content;

                    result.Add(new CuriaCandidateMeta
                    {
                        Path = file,
                        SourceType = CuriaSourceType.DecisionLog,
                        ProjectId = proj.Name,
                        Title = title,
                        Snippet = snippet,
                        LastModified = lastMod,
                    });
                }
                catch { }
            }
        }

        return result;
    }

    public async Task<string> ReadFullContentAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path)) return "";
        var content = await File.ReadAllTextAsync(path, Encoding.UTF8, ct);
        if (content.Length > 200_000) content = content[..200_000];
        return content;
    }
}
