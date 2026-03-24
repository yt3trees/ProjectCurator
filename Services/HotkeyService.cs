using System.Windows;
using System.Windows.Interop;
using ProjectCurator.Helpers;
using ProjectCurator.Models;

namespace ProjectCurator.Services;

public interface IHotkeyService
{
    bool HotkeyRegistered { get; }
    string HotkeyDisplayText { get; }
    Action? OnActivated { get; set; }
    Action? OnCaptureActivated { get; set; }
    void Register(Window window);
    void Unregister();
    void UpdateDisplay(string modifiers, string key);
    void ReRegister(string modifiers, string key);
    void ReRegisterCapture(string modifiers, string key);
}

public class HotkeyService : IHotkeyService
{
    private HotkeyConfig _config;
    private HotkeyConfig _captureConfig;
    private HwndSource? _hwndSource;
    private IntPtr _hwnd = IntPtr.Zero;

    public bool HotkeyRegistered { get; private set; }
    public bool CaptureHotkeyRegistered { get; private set; }
    public Action? OnActivated { get; set; }
    public Action? OnCaptureActivated { get; set; }

    /// <summary>現在のホットキー表示文字列 (例: "Ctrl+Shift+P")</summary>
    public string HotkeyDisplayText { get; private set; } = "";

    public HotkeyService(ConfigService configService)
    {
        var settings = configService.LoadSettings();
        _config = settings.Hotkey ?? new HotkeyConfig();
        _captureConfig = settings.CaptureHotkey ?? new HotkeyConfig { Modifiers = "Ctrl+Shift", Key = "C" };
        HotkeyDisplayText = BuildDisplayText(_config.Modifiers, _config.Key);
    }

    /// <summary>
    /// ホットキー設定を変更し、既に登録済みの場合は再登録する。
    /// SettingsPage からホットキー変更時に呼ぶ。
    /// </summary>
    public void ReRegister(string modifiers, string key)
    {
        _config.Modifiers = modifiers;
        _config.Key = key;
        HotkeyDisplayText = BuildDisplayText(modifiers, key);

        if (_hwnd != IntPtr.Zero)
        {
            Win32Interop.UnregisterHotKey(_hwnd, Win32Interop.HOTKEY_ID);
            var mods = ConvertModifiers(modifiers);
            var vk = ConvertVirtualKey(key);
            HotkeyRegistered = Win32Interop.RegisterHotKey(_hwnd, Win32Interop.HOTKEY_ID, mods, vk);
        }
    }

    /// <summary>
    /// キャプチャホットキー設定を変更し、既に登録済みの場合は再登録する。
    /// </summary>
    public void ReRegisterCapture(string modifiers, string key)
    {
        _captureConfig.Modifiers = modifiers;
        _captureConfig.Key = key;

        if (_hwnd != IntPtr.Zero)
        {
            Win32Interop.UnregisterHotKey(_hwnd, Win32Interop.CAPTURE_HOTKEY_ID);
            var mods = ConvertModifiers(modifiers);
            var vk = ConvertVirtualKey(key);
            CaptureHotkeyRegistered = Win32Interop.RegisterHotKey(_hwnd, Win32Interop.CAPTURE_HOTKEY_ID, mods, vk);
        }
    }

    public void Register(Window window)
    {
        _hwnd = new WindowInteropHelper(window).Handle;
        _hwndSource = HwndSource.FromHwnd(_hwnd);
        _hwndSource?.AddHook(WndProc);

        var modifiers = ConvertModifiers(_config.Modifiers);
        var vk = ConvertVirtualKey(_config.Key);
        Win32Interop.UnregisterHotKey(_hwnd, Win32Interop.HOTKEY_ID);
        HotkeyRegistered = Win32Interop.RegisterHotKey(_hwnd, Win32Interop.HOTKEY_ID, modifiers, vk);

        var captureMods = ConvertModifiers(_captureConfig.Modifiers);
        var captureVk = ConvertVirtualKey(_captureConfig.Key);
        Win32Interop.UnregisterHotKey(_hwnd, Win32Interop.CAPTURE_HOTKEY_ID);
        CaptureHotkeyRegistered = Win32Interop.RegisterHotKey(_hwnd, Win32Interop.CAPTURE_HOTKEY_ID, captureMods, captureVk);
    }

    public void Unregister()
    {
        if (_hwnd != IntPtr.Zero)
        {
            Win32Interop.UnregisterHotKey(_hwnd, Win32Interop.HOTKEY_ID);
            Win32Interop.UnregisterHotKey(_hwnd, Win32Interop.CAPTURE_HOTKEY_ID);
            HotkeyRegistered = false;
            CaptureHotkeyRegistered = false;
        }
        _hwndSource?.RemoveHook(WndProc);
        _hwndSource = null;
    }

    public void UpdateDisplay(string modifiers, string key)
    {
        _config.Modifiers = modifiers;
        _config.Key = key;
        HotkeyDisplayText = BuildDisplayText(modifiers, key);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32Interop.WM_HOTKEY)
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
        var parts = modifiers.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            result |= part.ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => Win32Interop.MOD_CONTROL,
                "ALT" => Win32Interop.MOD_ALT,
                "SHIFT" => Win32Interop.MOD_SHIFT,
                "WIN" or "WINDOWS" => Win32Interop.MOD_WIN,
                _ => 0u,
            };
        }
        return result;
    }

    private static uint ConvertVirtualKey(string key)
    {
        if (key.Length == 1)
        {
            var ch = char.ToUpperInvariant(key[0]);
            return (uint)ch;
        }

        if (key.StartsWith("F", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(key[1..], out var fNum) && fNum >= 1 && fNum <= 12)
        {
            return (uint)(0x70 + fNum - 1);
        }

        return key.ToUpperInvariant() switch
        {
            "SPACE" => 0x20u,
            "TAB" => 0x09u,
            "ESCAPE" or "ESC" => 0x1Bu,
            _ => 0u,
        };
    }

    private static string BuildDisplayText(string modifiers, string key)
        => string.IsNullOrWhiteSpace(modifiers) ? key : $"{modifiers}+{key}";
}
