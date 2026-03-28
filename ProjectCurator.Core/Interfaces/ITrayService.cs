namespace ProjectCurator.Interfaces;

public interface ITrayService
{
    void Initialize();
    void UpdateHotkeyDisplay(string hotkeyText);
    void ShowNotification(string title, string message);
}
