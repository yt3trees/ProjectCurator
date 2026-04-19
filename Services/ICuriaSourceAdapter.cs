using Curia.Models;

namespace Curia.Services;

public interface ICuriaSourceAdapter
{
    CuriaSourceType SourceType { get; }

    Task<List<CuriaCandidateMeta>> EnumerateCandidatesAsync(
        IEnumerable<ProjectInfo> projects,
        DateTime since,
        CancellationToken ct);

    Task<string> ReadFullContentAsync(string path, CancellationToken ct);
}
