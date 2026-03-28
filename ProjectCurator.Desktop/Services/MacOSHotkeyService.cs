using ProjectCurator.Interfaces;

namespace ProjectCurator.Desktop.Services;

public class MacOSHotkeyService : IHotkeyService
{
    private string _modifiers = "Cmd+Shift";
    private string _key = "P";

    public bool HotkeyRegistered => false;
    public string HotkeyDisplayText => $"{_modifiers}+{_key}";
    public Action? OnActivated { get; set; }
    public Action? OnCaptureActivated { get; set; }

    public void Register(IntPtr hwnd) { /* TODO: SharpHook Phase 3 */ }
    public void Unregister() { }
    public void UpdateDisplay(string modifiers, string key)
    {
        _modifiers = modifiers;
        _key = key;
    }
    public void ReRegister(string modifiers, string key)
    {
        _modifiers = modifiers;
        _key = key;
    }
    public void ReRegisterCapture(string modifiers, string key) { /* TODO: Phase 3 */ }
}
