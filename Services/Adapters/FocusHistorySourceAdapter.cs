using System.IO;
using System.Text;
using Curia.Models;

namespace Curia.Services.Adapters;

public class FocusHistorySourceAdapter : ICuriaSourceAdapter
{
    public CuriaSourceType SourceType => CuriaSourceType.FocusHistory;

    public async Task<List<CuriaCandidateMeta>> EnumerateCandidatesAsync(
        IEnumerable<ProjectInfo> projects,
        DateTime since,
        CancellationToken ct)
    {
        var result = new List<CuriaCandidateMeta>();

        foreach (var proj in projects)
        {
            var histDir = Path.Combine(proj.AiContextContentPath, "focus_history");
            if (!Directory.Exists(histDir)) continue;

            foreach (var file in Directory.GetFiles(histDir, "*.md"))
            {
                ct.ThrowIfCancellationRequested();
                var lastMod = File.GetLastWriteTime(file);
                if (lastMod < since) continue;

                try
                {
                    var content = await File.ReadAllTextAsync(file, Encoding.UTF8, ct);
                    var title = Path.GetFileNameWithoutExtension(file);
                    var snippet = content.Length > 500 ? content[..500] : content;

                    result.Add(new CuriaCandidateMeta
                    {
                        Path = file,
                        SourceType = CuriaSourceType.FocusHistory,
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
