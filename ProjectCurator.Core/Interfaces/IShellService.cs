namespace ProjectCurator.Interfaces;

public interface IShellService
{
    void OpenFolder(string path);
    void OpenFile(string path);
    void OpenTerminal(string path);
    void CreateSymlink(string linkPath, string targetPath);
    bool IsSymlink(string path);
    string? ResolveSymlinkTarget(string path);
    Task RunShellScriptAsync(string scriptPath, string arguments, Action<string> outputCallback, CancellationToken cancellationToken = default);
    void SetStartupEnabled(bool enabled, string appPath);
    bool IsStartupEnabled(string appPath);
}
