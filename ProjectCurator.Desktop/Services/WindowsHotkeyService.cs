using Avalonia.Controls;
using Avalonia.Win32;
using ProjectCurator.Desktop.Helpers;
using ProjectCurator.Interfaces;

namespace ProjectCurator.Desktop.Services;

/// <summary>
/// Windows global hotkey via Win32 RegisterHotKey + WndProc hook.
/// Matches the WPF HotkeyService approach.
/// </summary>
public class WindowsHotkeyService : IHotkeyService, IDisposable
{
    private IntPtr _hwnd;
    private Window? _window;
    private Win32Properties.CustomWndProcHookCallback? _wndProcCallback;

    private string _mainModifiers = "Ctrl+Shift";
    private string _mainKey = "P";
    private string _captureModifiers = "Ctrl+Shift";
    private string _captureKey = "C";

    public bool HotkeyRegistered { get; private set; }
    public string HotkeyDisplayText => BuildDisplayText(_mainModifiers, _mainKey);
    public Action? OnActivated { get; set; }
    public Action? OnCaptureActivated { get; set; }

    public void Register(Window window)
    {
        if (_window != null)
            return;

        _window = window;
        _hwnd = window.TryGetPlatformHandle()!.Handle;

        // Hook WndProc – fires on the UI thread (same as WPF HwndSource.AddHook).
        _wndProcCallback = WndProc;
        Win32Properties.AddWndProcHookCallback(window, _wndProcCallback);

        RegisterBothHotkeys();
    }

    public void Unregister()
    {
        if (_hwnd != IntPtr.Zero)
        {
            Win32Interop.UnregisterHotKey(_hwnd, Win32Interop.HOTKEY_ID);
            Win32Interop.UnregisterHotKey(_hwnd, Win32Interop.CAPTURE_HOTKEY_ID);
            _hwnd = IntPtr.Zero;
            HotkeyRegistered = false;
        }
        if (_window != null && _wndProcCallback != null)
        {
            Win32Properties.RemoveWndProcHookCallback(_window, _wndProcCallback);
            _window = null;
            _wndProcCallback = null;
        }
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
        if (_hwnd != IntPtr.Zero)
        {
            Win32Interop.UnregisterHotKey(_hwnd, Win32Interop.HOTKEY_ID);
            HotkeyRegistered = Win32Interop.RegisterHotKey(
                _hwnd, Win32Interop.HOTKEY_ID, ConvertModifiers(modifiers), ConvertVirtualKey(key));
        }
    }

    public void ReRegisterCapture(string modifiers, string key)
    {
        _captureModifiers = modifiers;
        _captureKey = key;
        if (_hwnd != IntPtr.Zero)
        {
            Win32Interop.UnregisterHotKey(_hwnd, Win32Interop.CAPTURE_HOTKEY_ID);
            Win32Interop.RegisterHotKey(
                _hwnd, Win32Interop.CAPTURE_HOTKEY_ID, ConvertModifiers(modifiers), ConvertVirtualKey(key));
        }
    }

    private void RegisterBothHotkeys()
    {
        Win32Interop.UnregisterHotKey(_hwnd, Win32Interop.HOTKEY_ID);
        Win32Interop.UnregisterHotKey(_hwnd, Win32Interop.CAPTURE_HOTKEY_ID);

        HotkeyRegistered = Win32Interop.RegisterHotKey(
            _hwnd, Win32Interop.HOTKEY_ID,
            ConvertModifiers(_mainModifiers), ConvertVirtualKey(_mainKey));

        Win32Interop.RegisterHotKey(
            _hwnd, Win32Interop.CAPTURE_HOTKEY_ID,
            ConvertModifiers(_captureModifiers), ConvertVirtualKey(_captureKey));
    }

    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == (uint)Win32Interop.WM_HOTKEY)
        {
            var id = wParam.ToInt32();
            if (id == Win32Interop.HOTKEY_ID)
            {
                OnActivated?.Invoke();
                handled = true;
            }
            else if (id == Win32Interop.CAPTURE_HOTKEY_ID)
            {
                OnCaptureActivated?.Invoke();
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    private static uint ConvertModifiers(string modifiers)
    {
        uint result = Win32Interop.MOD_NOREPEAT;
        foreach (var part in modifiers.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            result |= part.ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => Win32Interop.MOD_CONTROL,
                "ALT"               => Win32Interop.MOD_ALT,
                "SHIFT"             => Win32Interop.MOD_SHIFT,
                "WIN" or "WINDOWS"  => Win32Interop.MOD_WIN,
                _                   => 0u,
            };
        }
        return result;
    }

    private static uint ConvertVirtualKey(string key)
    {
        if (key.Length == 1)
            return (uint)char.ToUpperInvariant(key[0]);

        if (key.StartsWith("F", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(key[1..], out var fNum) && fNum is >= 1 and <= 12)
            return (uint)(0x70 + fNum - 1);

        return key.ToUpperInvariant() switch
        {
            "SPACE"          => 0x20u,
            "TAB"            => 0x09u,
            "ESCAPE" or "ESC"=> 0x1Bu,
            _                => 0u,
        };
    }

    private static string BuildDisplayText(string modifiers, string key)
        => string.IsNullOrWhiteSpace(modifiers) ? key : $"{modifiers}+{key}";

    public void Dispose() => Unregister();
}
