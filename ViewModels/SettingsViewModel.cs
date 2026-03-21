using System.Diagnostics;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectCurator.Models;
using ProjectCurator.Services;

namespace ProjectCurator.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ConfigService _configService;
    private readonly HotkeyService _hotkeyService;
    private readonly TrayService _trayService;
    private bool _loading;

    // ホットキー
    [ObservableProperty]
    private bool hotkeyCtrl = true;

    [ObservableProperty]
    private bool hotkeyShift = true;

    [ObservableProperty]
    private bool hotkeyAlt;

    [ObservableProperty]
    private bool hotkeyWin;

    [ObservableProperty]
    private string hotkeyKey = "P";

    // スタートアップ
    [ObservableProperty]
    private bool startupEnabled;

    // Dashboard
    [ObservableProperty]
    private int todayQueueLimit = 5;

    [ObservableProperty]
    private int autoRefreshMinutes;

    // Workspace paths
    [ObservableProperty]
    private string localProjectsRoot = "";

    [ObservableProperty]
    private string boxProjectsRoot = "";

    [ObservableProperty]
    private string obsidianVaultRoot = "";

    [ObservableProperty]
    private string workspacePathsWarning = "";

    // Asana global config (_config/asana_global.json)
    [ObservableProperty]
    private string asanaToken = "";

    [ObservableProperty]
    private string asanaWorkspaceGid = "";

    [ObservableProperty]
    private string asanaUserGid = "";

    [ObservableProperty]
    private string asanaPersonalProjectGids = "";

    [ObservableProperty]
    private string asanaOutputFile = "";

    [ObservableProperty]
    private string asanaGlobalStatus = "";

    // About
    public string AppVersion { get; } =
        "v" + (System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0");

    public System.Windows.Media.Imaging.BitmapSource? AppIcon => _trayService.DiamondBitmapSource;

    // ホットキー表示更新コールバック (TrayService.UpdateHotkeyDisplay を呼ぶ)
    public Action<string>? OnHotkeyDisplayChanged;

    public SettingsViewModel(ConfigService configService, HotkeyService hotkeyService, TrayService trayService)
    {
        _configService = configService;
        _hotkeyService = hotkeyService;
        _trayService = trayService;
    }

    /// <summary>ディスクから設定を読み込む。ページ表示時に呼ぶ。</summary>
    public void Load()
    {
        _loading = true;
        try
        {
            var settings = _configService.LoadSettings();
            TodayQueueLimit = settings.DashboardTodayQueueLimit;
            AutoRefreshMinutes = settings.DashboardAutoRefreshMinutes;
            LocalProjectsRoot = settings.LocalProjectsRoot;
            BoxProjectsRoot = settings.BoxProjectsRoot;
            ObsidianVaultRoot = settings.ObsidianVaultRoot;
            UpdateWorkspacePathsWarning();

            var hk = settings.Hotkey ?? new HotkeyConfig();
            var mods = hk.Modifiers.Split('+',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            HotkeyCtrl = mods.Contains("Ctrl", StringComparer.OrdinalIgnoreCase);
            HotkeyShift = mods.Contains("Shift", StringComparer.OrdinalIgnoreCase);
            HotkeyAlt = mods.Contains("Alt", StringComparer.OrdinalIgnoreCase);
            HotkeyWin = mods.Contains("Win", StringComparer.OrdinalIgnoreCase);
            HotkeyKey = hk.Key;

            StartupEnabled = File.Exists(GetStartupShortcutPath());

            var asana = _configService.LoadAsanaGlobalConfig();
            AsanaToken = asana.AsanaToken;
            AsanaWorkspaceGid = asana.WorkspaceGid;
            AsanaUserGid = asana.UserGid;
            AsanaPersonalProjectGids = string.Join("\n", asana.PersonalProjectGids);
            AsanaOutputFile = asana.OutputFile;
            AsanaGlobalStatus = "";
        }
        finally
        {
            _loading = false;
        }
    }

    partial void OnStartupEnabledChanged(bool value)
    {
        if (_loading) return;
        var shortcutPath = GetStartupShortcutPath();
        if (value) CreateStartupShortcut(shortcutPath);
        else RemoveStartupShortcut(shortcutPath);
    }

    [RelayCommand]
    public void ApplyHotkey()
    {
        var mods = new List<string>();
        if (HotkeyCtrl) mods.Add("Ctrl");
        if (HotkeyShift) mods.Add("Shift");
        if (HotkeyAlt) mods.Add("Alt");
        if (HotkeyWin) mods.Add("Win");
        var modStr = string.Join("+", mods);
        if (string.IsNullOrWhiteSpace(HotkeyKey)) return;

        _hotkeyService.ReRegister(modStr, HotkeyKey.Trim());
        OnHotkeyDisplayChanged?.Invoke(_hotkeyService.HotkeyDisplayText);

        var settings = _configService.LoadSettings();
        settings.Hotkey = new HotkeyConfig { Modifiers = modStr, Key = HotkeyKey.Trim() };
        _configService.SaveSettings(settings);
    }

    [RelayCommand]
    public void Save()
    {
        var settings = _configService.LoadSettings();
        settings.DashboardTodayQueueLimit = TodayQueueLimit;
        settings.DashboardAutoRefreshMinutes = AutoRefreshMinutes;
        settings.LocalProjectsRoot = LocalProjectsRoot.Trim();
        settings.BoxProjectsRoot = BoxProjectsRoot.Trim();
        settings.ObsidianVaultRoot = ObsidianVaultRoot.Trim();
        _configService.SaveSettings(settings);
        UpdateWorkspacePathsWarning();
    }

    [RelayCommand]
    public void SaveAsanaGlobal()
    {
        try
        {
            var gids = AsanaPersonalProjectGids
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToList();

            var config = new AsanaGlobalConfig
            {
                AsanaToken = AsanaToken.Trim(),
                WorkspaceGid = AsanaWorkspaceGid.Trim(),
                UserGid = AsanaUserGid.Trim(),
                PersonalProjectGids = gids,
                OutputFile = AsanaOutputFile.Trim()
            };

            _configService.SaveAsanaGlobalConfig(config);
            AsanaGlobalStatus = $"Saved {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            AsanaGlobalStatus = $"Error: {ex.Message}";
        }
    }

    private static string GetStartupShortcutPath()
    {
        var startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        return Path.Combine(startupFolder, "ProjectCurator.lnk");
    }

    private static void CreateStartupShortcut(string shortcutPath)
    {
        try
        {
            var exePath = Process.GetCurrentProcess().MainModule!.FileName;
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;
            var shell = (dynamic)Activator.CreateInstance(shellType)!;
            var shortcut = shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = exePath;
            shortcut.WorkingDirectory = Path.GetDirectoryName(exePath) ?? "";
            shortcut.Save();
        }
        catch { }
    }

    private static void RemoveStartupShortcut(string shortcutPath)
    {
        try { File.Delete(shortcutPath); }
        catch { }
    }

    partial void OnLocalProjectsRootChanged(string value)
    {
        if (_loading) return;
        UpdateWorkspacePathsWarning();
    }

    partial void OnBoxProjectsRootChanged(string value)
    {
        if (_loading) return;
        UpdateWorkspacePathsWarning();
    }

    partial void OnObsidianVaultRootChanged(string value)
    {
        if (_loading) return;
        UpdateWorkspacePathsWarning();
    }

    private void UpdateWorkspacePathsWarning()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(LocalProjectsRoot)) missing.Add("Local Projects Root");
        if (string.IsNullOrWhiteSpace(BoxProjectsRoot)) missing.Add("Box Projects Root");
        if (string.IsNullOrWhiteSpace(ObsidianVaultRoot)) missing.Add("Obsidian Vault Root");

        WorkspacePathsWarning = missing.Count == 0
            ? ""
            : $"Missing required paths: {string.Join(", ", missing)}. Some features may not work until saved.";
    }
}
