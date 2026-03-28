using System.Diagnostics;
using System.Text;
using ProjectCurator.Interfaces;

namespace ProjectCurator.Services;

public class ScriptRunnerService(IDispatcherService dispatcher)
{
    private readonly IDispatcherService _dispatcher = dispatcher;

    public async Task RunPowerShellAsync(string scriptPath, string args, Action<string>? onOutput = null, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" {args}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        await RunProcessAsync(psi, onOutput, ct);
    }

    public async Task RunPythonAsync(string scriptPath, string args, Action<string>? onOutput = null, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "python",
            Arguments = $"\"{scriptPath}\" {args}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
        await RunProcessAsync(psi, onOutput, ct);
    }

    private async Task RunProcessAsync(ProcessStartInfo psi, Action<string>? onOutput, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        void Dispatch(string? line)
        {
            if (line == null || onOutput == null) return;
            _dispatcher.Post(() => onOutput(line));
        }

        process.OutputDataReceived += (_, e) => Dispatch(e.Data);
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) Dispatch($"[ERR] {e.Data}"); };
        process.Exited += (_, _) => tcs.TrySetResult(process.ExitCode);

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await using var _ = ct.Register(() =>
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            tcs.TrySetCanceled(ct);
        });

        await tcs.Task;
    }
}
