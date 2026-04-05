using Curia.Helpers;

namespace Curia.Services;

/// <summary>
/// EncodingDetector の非同期ラッパー。
/// ファイルI/Oを Task.Run でバックグラウンドに委譲し、UIスレッドをブロックしない。
/// </summary>
public class FileEncodingService
{
    public async Task<(string content, string encoding)> ReadFileAsync(
        string path, CancellationToken ct = default)
        => await Task.Run(() => EncodingDetector.ReadFile(path), ct);

    public async Task WriteFileAsync(
        string path, string content, string encoding, CancellationToken ct = default)
        => await Task.Run(() => EncodingDetector.WriteFile(path, content, encoding), ct);

    public (string content, string encoding) ReadFile(string path)
        => EncodingDetector.ReadFile(path);

    public void WriteFile(string path, string content, string encoding)
        => EncodingDetector.WriteFile(path, content, encoding);
}
