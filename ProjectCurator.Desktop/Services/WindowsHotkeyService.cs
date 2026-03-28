using System.Runtime.InteropServices;
using ProjectCurator.Interfaces;

namespace ProjectCurator.Desktop.Services;

public class WindowsHotkeyService : IHotkeyService, IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_MAIN = 1;
    private const int HOTKEY_CAPTURE = 2;

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private IntPtr _hwnd = IntPtr.Zero;
    private bool _registered = false;

    private string _mainModifiers = "Ctrl+Shift";
    private string _mainKey = "P";
    private string _captureModifiers = "Ctrl+Shift";
    private string _captureKey = "C";

    public bool HotkeyRegistered => _registered;
    public string HotkeyDisplayText => $"{_mainModifiers}+{_mainKey}";
    public Action? OnActivated { get; set; }
    public Action? OnCaptureActivated { get; set; }

    private static uint ParseModifiers(string modifiers)
    {
        uint mods = 0;
        if (modifiers.Contains("Ctrl", StringComparison.OrdinalIgnoreCase)) mods |= 0x2;
        if (modifiers.Contains("Shift", StringComparison.OrdinalIgnoreCase)) mods |= 0x4;
        if (modifiers.Contains("Alt", StringComparison.OrdinalIgnoreCase)) mods |= 0x1;
        if (modifiers.Contains("Win", StringComparison.OrdinalIgnoreCase)) mods |= 0x8;
        return mods;
    }

    private static uint ParseVirtualKey(string key)
    {
        if (key.Length == 1 && char.IsLetter(key[0]))
            return (uint)char.ToUpper(key[0]);
        return key.ToUpper() switch
        {
            "F1" => 0x70, "F2" => 0x71, "F3" => 0x72, "F4" => 0x73,
            "F5" => 0x74, "F6" => 0x75, "F7" => 0x76, "F8" => 0x77,
            "F9" => 0x78, "F10" => 0x79, "F11" => 0x7A, "F12" => 0x7B,
            _ => 0
        };
    }

    public void Register(IntPtr hwnd)
    {
        _hwnd = hwnd;
        var mainMods = ParseModifiers(_mainModifiers);
        var mainVk = ParseVirtualKey(_mainKey);
        if (mainVk != 0)
            _registered = RegisterHotKey(_hwnd, HOTKEY_MAIN, mainMods, mainVk);

        var captureMods = ParseModifiers(_captureModifiers);
        var captureVk = ParseVirtualKey(_captureKey);
        if (captureVk != 0)
            RegisterHotKey(_hwnd, HOTKEY_CAPTURE, captureMods, captureVk);
    }

    public void Unregister()
    {
        if (_hwnd != IntPtr.Zero)
        {
            UnregisterHotKey(_hwnd, HOTKEY_MAIN);
            UnregisterHotKey(_hwnd, HOTKEY_CAPTURE);
            _registered = false;
        }
    }

    public void UpdateDisplay(string modifiers, string key)
    {
        _mainModifiers = modifiers;
        _mainKey = key;
    }

    public void ReRegister(string modifiers, string key)
    {
        if (_hwnd != IntPtr.Zero)
            UnregisterHotKey(_hwnd, HOTKEY_MAIN);
        _mainModifiers = modifiers;
        _mainKey = key;
        if (_hwnd != IntPtr.Zero)
        {
            var mods = ParseModifiers(modifiers);
            var vk = ParseVirtualKey(key);
            if (vk != 0) _registered = RegisterHotKey(_hwnd, HOTKEY_MAIN, mods, vk);
        }
    }

    public void ReRegisterCapture(string modifiers, string key)
    {
        if (_hwnd != IntPtr.Zero)
            UnregisterHotKey(_hwnd, HOTKEY_CAPTURE);
        _captureModifiers = modifiers;
        _captureKey = key;
        if (_hwnd != IntPtr.Zero)
        {
            var mods = ParseModifiers(modifiers);
            var vk = ParseVirtualKey(key);
            if (vk != 0) RegisterHotKey(_hwnd, HOTKEY_CAPTURE, mods, vk);
        }
    }

    public void ProcessMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_HOTKEY)
        {
            if (wParam.ToInt32() == HOTKEY_MAIN) OnActivated?.Invoke();
            else if (wParam.ToInt32() == HOTKEY_CAPTURE) OnCaptureActivated?.Invoke();
        }
    }

    public void Dispose() => Unregister();
}
