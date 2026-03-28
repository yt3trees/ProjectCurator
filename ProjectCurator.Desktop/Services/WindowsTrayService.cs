using ProjectCurator.Interfaces;

namespace ProjectCurator.Desktop.Services;

public class WindowsTrayService : ITrayService
{
    public void Initialize() { /* Avalonia TrayIcon is defined in App.axaml */ }
    public void UpdateHotkeyDisplay(string hotkeyText) { /* TODO: Phase 3 */ }
    public void ShowNotification(string title, string message) { /* TODO: Phase 3 */ }
}
