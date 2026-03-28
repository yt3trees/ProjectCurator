using System.Diagnostics;
using ProjectCurator.Interfaces;

namespace ProjectCurator.Desktop.Services;

public class WindowsShellService : IShellService
{
    public void OpenFolder(string path)
    {
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }

    public void OpenFile(string path)
    {
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    public void OpenTerminal(string path)
    {
        // Try Windows Terminal, fallback to PowerShell
        try { Process.Start(new ProcessStartInfo("wt.exe", $"-d \"{path}\"") { UseShellExecute = true }); }
        catch { Process.Start(new ProcessStartInfo("pwsh.exe", $"-NoExit -Command \"Set-Location '{path}'\"") { UseShellExecute = true }); }
    }

    public void CreateSymlink(string linkPath, string targetPath)
    {
        var psCommand = $"New-Item -ItemType Junction -Path '{linkPath}' -Target '{targetPath}' -Force | Out-Null";
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{psCommand}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        })!;
        process.WaitForExit();
    }

    public bool IsSymlink(string path)
    {
        if (Directory.Exists(path))
            return (new DirectoryInfo(path).Attributes & FileAttributes.ReparsePoint) != 0;
        if (File.Exists(path))
            return (new FileInfo(path).Attributes & FileAttributes.ReparsePoint) != 0;
        return false;
    }

    public string? ResolveSymlinkTarget(string path)
    {
        try { return Directory.ResolveLinkTarget(path, true)?.FullName; }
        catch { return null; }
    }

    public async Task RunShellScriptAsync(string scriptPath, string arguments, Action<string> outputCallback, CancellationToken cancellationToken = default)
    {
        var ext = Path.GetExtension(scriptPath).ToLower();
        var fileName = ext == ".py" ? "python" : "powershell.exe";
        var args = ext == ".py" ? $"\"{scriptPath}\" {arguments}" : $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" {arguments}";

        var psi = new ProcessStartInfo(fileName, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) => { if (e.Data != null) outputCallback(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) outputCallback($"[ERR] {e.Data}"); };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken);
    }

    public void SetStartupEnabled(bool enabled, string appPath)
    {
        var startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        var shortcutPath = Path.Combine(startupFolder, "ProjectCurator.lnk");
        if (enabled)
        {
            var psCommand = $"$ws = New-Object -ComObject WScript.Shell; $s = $ws.CreateShortcut('{shortcutPath}'); $s.TargetPath = '{appPath}'; $s.Save()";
            Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -Command \"{psCommand}\"") { UseShellExecute = false, CreateNoWindow = true })?.WaitForExit();
        }
        else
        {
            if (File.Exists(shortcutPath)) File.Delete(shortcutPath);
        }
    }

    public bool IsStartupEnabled(string appPath)
    {
        var startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        var shortcutPath = Path.Combine(startupFolder, "ProjectCurator.lnk");
        return File.Exists(shortcutPath);
    }
}
