using SharpHook;
using SharpHook.Native;
using ProjectCurator.Interfaces;

namespace ProjectCurator.Desktop.Services;

public class MacOSHotkeyService : IHotkeyService, IDisposable
{
    private TaskPoolGlobalHook? _hook;
    private CancellationTokenSource? _cts;
    private bool _registered = false;

    private string _mainModifiers = "Cmd+Shift";
    private string _mainKey = "P";
    private string _captureModifiers = "Cmd+Shift";
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
        if (modifiers.Contains("Cmd", StringComparison.OrdinalIgnoreCase) ||
            modifiers.Contains("Meta", StringComparison.OrdinalIgnoreCase) ||
            modifiers.Contains("Win", StringComparison.OrdinalIgnoreCase))
            mask |= ModifierMask.Meta;
        if (modifiers.Contains("Ctrl", StringComparison.OrdinalIgnoreCase))
            mask |= ModifierMask.Ctrl;
        if (modifiers.Contains("Alt", StringComparison.OrdinalIgnoreCase) ||
            modifiers.Contains("Option", StringComparison.OrdinalIgnoreCase))
            mask |= ModifierMask.Alt;
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
            "F1"  => KeyCode.VcF1,  "F2"  => KeyCode.VcF2,  "F3"  => KeyCode.VcF3,
            "F4"  => KeyCode.VcF4,  "F5"  => KeyCode.VcF5,  "F6"  => KeyCode.VcF6,
            "F7"  => KeyCode.VcF7,  "F8"  => KeyCode.VcF8,  "F9"  => KeyCode.VcF9,
            "F10" => KeyCode.VcF10, "F11" => KeyCode.VcF11, "F12" => KeyCode.VcF12,
            _ => KeyCode.VcUndefined
        };
    }

    private bool IsMatch(KeyboardHookEventArgs e, ModifierMask expectedMask, KeyCode expectedKey)
    {
        if (e.Data.KeyCode != expectedKey)
            return false;
        var actualMask = e.RawEvent.Mask;
        // Check that all expected modifier bits are set; ignore extra bits.
        return (actualMask & expectedMask) == expectedMask;
    }

    public void Register(IntPtr hwnd)
    {
        StartHookAsync();
    }

    private void StartHookAsync()
    {
        StopHook();

        _cts = new CancellationTokenSource();
        _hook = new TaskPoolGlobalHook(globalHookType: GlobalHookType.Keyboard);
        _hook.KeyPressed += OnKeyPressed;

        _ = Task.Run(async () =>
        {
            try
            {
                await _hook.RunAsync();
                _registered = true;
            }
            catch (Exception)
            {
                // macOS Accessibility permission not granted or hook failed.
                _registered = false;
            }
        }, _cts.Token);

        // Optimistically mark as registered; will be corrected if RunAsync throws.
        _registered = true;
    }

    private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        var mainMask = ParseModifiers(_mainModifiers);
        var mainKey  = ParseKeyCode(_mainKey);
        if (mainKey != KeyCode.VcUndefined && IsMatch(e, mainMask, mainKey))
        {
            OnActivated?.Invoke();
            return;
        }

        var captureMask = ParseModifiers(_captureModifiers);
        var captureKey  = ParseKeyCode(_captureKey);
        if (captureKey != KeyCode.VcUndefined && IsMatch(e, captureMask, captureKey))
        {
            OnCaptureActivated?.Invoke();
        }
    }

    public void Unregister()
    {
        StopHook();
        _registered = false;
    }

    private void StopHook()
    {
        if (_hook != null)
        {
            _hook.KeyPressed -= OnKeyPressed;
            try { _hook.Dispose(); } catch { /* ignore */ }
            _hook = null;
        }
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
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
        // Re-start the hook so the new key combination takes effect.
        StartHookAsync();
    }

    public void ReRegisterCapture(string modifiers, string key)
    {
        _captureModifiers = modifiers;
        _captureKey = key;
    }

    public void Dispose() => StopHook();
}
