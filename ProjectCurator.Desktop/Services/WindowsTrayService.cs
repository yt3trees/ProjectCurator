using ProjectCurator.Interfaces;

namespace ProjectCurator.Desktop.Services;

public class WindowsTrayService : ITrayService
{
    public void Initialize() { /* Avalonia TrayIcon is defined in App.axaml */ }
    public void UpdateHotkeyDisplay(string hotkeyText)
    {
        if (Avalonia.Application.Current is ProjectCurator.Desktop.App app)
            app.UpdateTrayHotkeyDisplay(hotkeyText);
    }
    public void ShowNotification(string title, string message)
    {
        // TODO: Avalonia TrayIcon does not expose a direct notification API.
        // Consider using a platform-specific notification library (e.g. NativeNotification)
        // or the Windows ToastNotification API via WinRT interop in a future phase.
    }
}
