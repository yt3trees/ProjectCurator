using SharpHook;
using SharpHook.Native;
using ProjectCurator.Interfaces;

namespace ProjectCurator.Desktop.Services;

public class WindowsHotkeyService : IHotkeyService, IDisposable
{
    private TaskPoolGlobalHook? _hook;
    private bool _registered;

    private string _mainModifiers = "Ctrl+Shift";
    private string _mainKey = "P";
    private string _captureModifiers = "Ctrl+Shift";
    private string _captureKey = "C";

    public bool HotkeyRegistered => _registered;
    public string HotkeyDisplayText => $"{_mainModifiers}+{_mainKey}";
    public Action? OnActivated { get; set; }
    public Action? OnCaptureActivated { get; set; }

    private static ModifierMask ParseModifiers(string modifiers)
    {
        ModifierMask mask = ModifierMask.None;
        if (modifiers.Contains("Shift", StringComparison.OrdinalIgnoreCase))
            mask |= ModifierMask.Shift;
        if (modifiers.Contains("Ctrl", StringComparison.OrdinalIgnoreCase))
            mask |= ModifierMask.Ctrl;
        if (modifiers.Contains("Alt", StringComparison.OrdinalIgnoreCase))
            mask |= ModifierMask.Alt;
        if (modifiers.Contains("Win", StringComparison.OrdinalIgnoreCase) ||
            modifiers.Contains("Meta", StringComparison.OrdinalIgnoreCase))
            mask |= ModifierMask.Meta;
        return mask;
    }

    private static KeyCode ParseKeyCode(string key)
    {
        if (key.Length == 1)
        {
            return key.ToUpper() switch
            {
                "A" => KeyCode.VcA, "B" => KeyCode.VcB, "C" => KeyCode.VcC,
                "D" => KeyCode.VcD, "E" => KeyCode.VcE, "F" => KeyCode.VcF,
                "G" => KeyCode.VcG, "H" => KeyCode.VcH, "I" => KeyCode.VcI,
                "J" => KeyCode.VcJ, "K" => KeyCode.VcK, "L" => KeyCode.VcL,
                "M" => KeyCode.VcM, "N" => KeyCode.VcN, "O" => KeyCode.VcO,
                "P" => KeyCode.VcP, "Q" => KeyCode.VcQ, "R" => KeyCode.VcR,
                "S" => KeyCode.VcS, "T" => KeyCode.VcT, "U" => KeyCode.VcU,
                "V" => KeyCode.VcV, "W" => KeyCode.VcW, "X" => KeyCode.VcX,
                "Y" => KeyCode.VcY, "Z" => KeyCode.VcZ,
                _ => KeyCode.VcUndefined
            };
        }

        return key.ToUpper() switch
        {
            "F1" => KeyCode.VcF1, "F2" => KeyCode.VcF2, "F3" => KeyCode.VcF3, "F4" => KeyCode.VcF4,
            "F5" => KeyCode.VcF5, "F6" => KeyCode.VcF6, "F7" => KeyCode.VcF7, "F8" => KeyCode.VcF8,
            "F9" => KeyCode.VcF9, "F10" => KeyCode.VcF10, "F11" => KeyCode.VcF11, "F12" => KeyCode.VcF12,
            _ => KeyCode.VcUndefined
        };
    }

    private bool IsMatch(KeyboardHookEventArgs e, ModifierMask expectedMask, KeyCode expectedKey)
    {
        if (e.Data.KeyCode != expectedKey)
            return false;
        var actualMask = e.RawEvent.Mask;
        return (actualMask & expectedMask) == expectedMask;
    }

    public void Register(IntPtr hwnd)
    {
        if (_hook != null)
            return;

        _hook = new TaskPoolGlobalHook(globalHookType: GlobalHookType.Keyboard);
        _hook.KeyPressed += OnKeyPressed;
        _ = Task.Run(async () =>
        {
            try
            {
                await _hook.RunAsync();
                _registered = true;
            }
            catch
            {
                _registered = false;
            }
        });
        _registered = true;
    }

    public void Unregister()
    {
        if (_hook != null)
        {
            _hook.KeyPressed -= OnKeyPressed;
            try { _hook.Dispose(); } catch { }
            _hook = null;
        }
        _registered = false;
    }

    public void UpdateDisplay(string modifiers, string key)
    {
        _mainModifiers = modifiers;
        _mainKey = key;
    }

    public void ReRegister(string modifiers, string key)
    {
        _mainModifiers = modifiers;
        _mainKey = key;
    }

    public void ReRegisterCapture(string modifiers, string key)
    {
        _captureModifiers = modifiers;
        _captureKey = key;
    }

    private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        var mainMask = ParseModifiers(_mainModifiers);
        var mainKey = ParseKeyCode(_mainKey);
        if (mainKey != KeyCode.VcUndefined && IsMatch(e, mainMask, mainKey))
        {
            OnActivated?.Invoke();
            return;
        }

        var captureMask = ParseModifiers(_captureModifiers);
        var captureKey = ParseKeyCode(_captureKey);
        if (captureKey != KeyCode.VcUndefined && IsMatch(e, captureMask, captureKey))
            OnCaptureActivated?.Invoke();
    }

    public void Dispose() => Unregister();
}
