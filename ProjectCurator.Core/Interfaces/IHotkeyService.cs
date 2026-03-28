namespace ProjectCurator.Interfaces;

public interface IHotkeyService
{
    bool HotkeyRegistered { get; }
    string HotkeyDisplayText { get; }
    Action? OnActivated { get; set; }
    Action? OnCaptureActivated { get; set; }
    void Unregister();
    void UpdateDisplay(string modifiers, string key);
    void ReRegister(string modifiers, string key);
    void ReRegisterCapture(string modifiers, string key);
}
