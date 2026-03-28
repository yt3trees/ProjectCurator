using System.Diagnostics;
using System.IO;
using System.Text;
using ProjectCurator.Interfaces;
using WpfApp = System.Windows.Application;
using WpfMsgBox = System.Windows.MessageBox;
using WpfMsgBoxButton = System.Windows.MessageBoxButton;
using WpfMsgBoxResult = System.Windows.MessageBoxResult;
using WpfMsgBoxImage = System.Windows.MessageBoxImage;

namespace ProjectCurator.Services;

// WPF-specific IDispatcherService implementation
public class WpfDispatcherService : IDispatcherService
{
    public void Post(Action action)
    {
        var app = WpfApp.Current;
        if (app == null) return;
        if (app.Dispatcher.CheckAccess()) action();
        else app.Dispatcher.BeginInvoke(action);
    }

    public void Invoke(Action action)
    {
        var app = WpfApp.Current;
        if (app == null) return;
        if (app.Dispatcher.CheckAccess()) action();
        else app.Dispatcher.Invoke(action);
    }

    public Task InvokeAsync(Action action)
    {
        var app = WpfApp.Current;
        if (app == null) return Task.CompletedTask;
        return app.Dispatcher.InvokeAsync(action).Task;
    }
}

// WPF-specific IDialogService implementation
public class WpfDialogService : IDialogService
{
    public Task ShowMessageAsync(string title, string message)
    {
        WpfMsgBox.Show(message, title, WpfMsgBoxButton.OK, WpfMsgBoxImage.Information);
        return Task.CompletedTask;
    }

    public Task<bool> ShowConfirmAsync(string title, string message)
    {
        var result = WpfMsgBox.Show(message, title, WpfMsgBoxButton.YesNo, WpfMsgBoxImage.Question);
        return Task.FromResult(result == WpfMsgBoxResult.Yes);
    }
}

// WPF-specific IShellService implementation (Windows only)
public class WpfShellService : IShellService
{
    public void OpenFolder(string path)
        => Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });

    public void OpenFile(string path)
        => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

    public void OpenTerminal(string path)
    {
        try { Process.Start(new ProcessStartInfo("wt.exe", $"-d \"{path}\"") { UseShellExecute = true }); }
        catch { Process.Start(new ProcessStartInfo("pwsh.exe", $"-NoExit -Command \"Set-Location '{path}'\"") { UseShellExecute = true }); }
    }

    public void CreateSymlink(string linkPath, string targetPath)
    {
        var psCmd = $"New-Item -ItemType Junction -Path '{linkPath}' -Target '{targetPath}' -Force | Out-Null";
        using var p = Process.Start(new ProcessStartInfo("powershell.exe",
            $"-NoProfile -ExecutionPolicy Bypass -Command \"{psCmd}\"")
        { UseShellExecute = false, CreateNoWindow = true })!;
        p.WaitForExit();
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
        var (fileName, args) = ext == ".py"
            ? ("python", $"\"{scriptPath}\" {arguments}")
            : ("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" {arguments}");

        var psi = new ProcessStartInfo(fileName, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
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
            var psCmd = $"$ws = New-Object -ComObject WScript.Shell; $s = $ws.CreateShortcut('{shortcutPath}'); $s.TargetPath = '{appPath}'; $s.Save()";
            Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -Command \"{psCmd}\"")
            { UseShellExecute = false, CreateNoWindow = true })?.WaitForExit();
        }
        else
        {
            if (File.Exists(shortcutPath)) File.Delete(shortcutPath);
        }
    }

    public bool IsStartupEnabled(string appPath)
    {
        var startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        return File.Exists(Path.Combine(startupFolder, "ProjectCurator.lnk"));
    }
}
