using ProjectCurator.Interfaces;

namespace ProjectCurator.Desktop.Services;

public class MacOSTrayService : ITrayService
{
    public void Initialize() { }
    public void UpdateHotkeyDisplay(string hotkeyText) { }
    public void ShowNotification(string title, string message) { }
}
