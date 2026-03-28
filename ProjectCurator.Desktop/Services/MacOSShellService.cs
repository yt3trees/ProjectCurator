using System.Diagnostics;
using ProjectCurator.Interfaces;

namespace ProjectCurator.Desktop.Services;

public class MacOSShellService : IShellService
{
    public void OpenFolder(string path) => Process.Start(new ProcessStartInfo("open", $"\"{path}\"") { UseShellExecute = false });
    public void OpenFile(string path) => Process.Start(new ProcessStartInfo("open", $"\"{path}\"") { UseShellExecute = false });
    public void OpenTerminal(string path) => Process.Start(new ProcessStartInfo("open", $"-a Terminal \"{path}\"") { UseShellExecute = false });
    public void CreateSymlink(string linkPath, string targetPath) => Directory.CreateSymbolicLink(linkPath, targetPath);

    public bool IsSymlink(string path)
    {
        if (File.Exists(path)) return new FileInfo(path).LinkTarget != null;
        if (Directory.Exists(path)) return new DirectoryInfo(path).LinkTarget != null;
        return false;
    }

    public string? ResolveSymlinkTarget(string path)
    {
        try { return Directory.ResolveLinkTarget(path, true)?.FullName; }
        catch { return null; }
    }

    public async Task RunShellScriptAsync(string scriptPath, string arguments, Action<string> outputCallback, CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo("/bin/bash", $"\"{scriptPath}\" {arguments}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) => { if (e.Data != null) outputCallback(e.Data); };
        process.Start();
        process.BeginOutputReadLine();
        await process.WaitForExitAsync(cancellationToken);
    }

    public void SetStartupEnabled(bool enabled, string appPath) { /* TODO: LaunchAgent Phase 3 */ }
    public bool IsStartupEnabled(string appPath) => false;
}
