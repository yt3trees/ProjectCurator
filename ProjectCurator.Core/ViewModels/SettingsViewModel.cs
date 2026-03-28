using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using ProjectCurator.Interfaces;
using ProjectCurator.Models;
using ProjectCurator.Services;

namespace ProjectCurator.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ConfigService _configService;
    private readonly IHotkeyService _hotkeyService;
    private readonly ITrayService _trayService;
    private readonly LlmClientService _llmClientService;
    private readonly IShellService _shellService;
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

    // キャプチャホットキー
    [ObservableProperty]
    private bool captureHotkeyCtrl = true;

    [ObservableProperty]
    private bool captureHotkeyShift = true;

    [ObservableProperty]
    private bool captureHotkeyAlt;

    [ObservableProperty]
    private bool captureHotkeyWin;

    [ObservableProperty]
    private string captureHotkeyKey = "C";

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
    private string cloudSyncRoot = "";

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

    // LLM API settings
    [ObservableProperty]
    private string llmProvider = "openai";

    [ObservableProperty]
    private string llmApiKey = "";

    [ObservableProperty]
    private string llmModel = "gpt-4o";

    [ObservableProperty]
    private string llmEndpoint = "";

    [ObservableProperty]
    private string llmApiVersion = "2024-12-01-preview";

    [ObservableProperty]
    private string llmParametersText = "";

    [ObservableProperty]
    private string llmUserProfile = "";

    [ObservableProperty]
    private string llmStatus = "";

    [ObservableProperty]
    private bool llmIsAzure;

    [ObservableProperty]
    private bool aiEnabled;

    // Test Connection が成功するまでトグルを有効化できない
    [ObservableProperty]
    private bool aiToggleCanEnable;

    // Capture
    [ObservableProperty]
    private bool captureTaskLogEnabled;

    // About
    public string AppVersion { get; } =
        "v" + (System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0");

    // ホットキー表示更新コールバック (TrayService.UpdateHotkeyDisplay を呼ぶ)
    public Action<string>? OnHotkeyDisplayChanged;

    public SettingsViewModel(
        ConfigService configService,
        IHotkeyService hotkeyService,
        ITrayService trayService,
        LlmClientService llmClientService,
        IShellService shellService)
    {
        _configService = configService;
        _hotkeyService = hotkeyService;
        _trayService = trayService;
        _llmClientService = llmClientService;
        _shellService = shellService;
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
            CloudSyncRoot = settings.CloudSyncRoot;
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

            var chk = settings.CaptureHotkey ?? new HotkeyConfig { Modifiers = "Ctrl+Shift", Key = "C" };
            var capMods = chk.Modifiers.Split('+',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            CaptureHotkeyCtrl = capMods.Contains("Ctrl", StringComparer.OrdinalIgnoreCase);
            CaptureHotkeyShift = capMods.Contains("Shift", StringComparer.OrdinalIgnoreCase);
            CaptureHotkeyAlt = capMods.Contains("Alt", StringComparer.OrdinalIgnoreCase);
            CaptureHotkeyWin = capMods.Contains("Win", StringComparer.OrdinalIgnoreCase);
            CaptureHotkeyKey = chk.Key;

            var exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
            StartupEnabled = _shellService.IsStartupEnabled(exePath);

            var asana = _configService.LoadAsanaGlobalConfig();
            AsanaToken = asana.AsanaToken;
            AsanaWorkspaceGid = asana.WorkspaceGid;
            AsanaUserGid = asana.UserGid;
            AsanaPersonalProjectGids = string.Join("\n", asana.PersonalProjectGids);
            AsanaOutputFile = asana.OutputFile;
            AsanaGlobalStatus = "";

            // LLM
            LlmProvider    = settings.LlmProvider;
            LlmApiKey      = settings.LlmApiKey;
            LlmModel       = settings.LlmModel;
            LlmEndpoint    = settings.LlmEndpoint;
            LlmApiVersion  = settings.LlmApiVersion;
            LlmParametersText = string.Join("\n",
                settings.LlmParameters.Select(kv => $"{kv.Key} = {kv.Value}"));
            LlmUserProfile     = settings.LlmUserProfile;
            LlmIsAzure         = settings.LlmProvider.Equals("azure_openai", StringComparison.OrdinalIgnoreCase);
            LlmStatus          = "";
            AiEnabled          = settings.AiEnabled;
            AiToggleCanEnable  = settings.AiEnabled;
            CaptureTaskLogEnabled = settings.CaptureTaskLogEnabled;
        }
        finally
        {
            _loading = false;
        }
    }

    partial void OnStartupEnabledChanged(bool value)
    {
        if (_loading) return;
        var exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
        _shellService.SetStartupEnabled(value, exePath);
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
    public void ApplyCaptureHotkey()
    {
        var mods = new List<string>();
        if (CaptureHotkeyCtrl) mods.Add("Ctrl");
        if (CaptureHotkeyShift) mods.Add("Shift");
        if (CaptureHotkeyAlt) mods.Add("Alt");
        if (CaptureHotkeyWin) mods.Add("Win");
        var modStr = string.Join("+", mods);
        if (string.IsNullOrWhiteSpace(CaptureHotkeyKey)) return;

        _hotkeyService.ReRegisterCapture(modStr, CaptureHotkeyKey.Trim());

        var settings = _configService.LoadSettings();
        settings.CaptureHotkey = new HotkeyConfig { Modifiers = modStr, Key = CaptureHotkeyKey.Trim() };
        _configService.SaveSettings(settings);
    }

    [RelayCommand]
    public void Save()
    {
        var settings = _configService.LoadSettings();
        settings.DashboardTodayQueueLimit = TodayQueueLimit;
        settings.DashboardAutoRefreshMinutes = AutoRefreshMinutes;
        settings.LocalProjectsRoot = LocalProjectsRoot.Trim();
        settings.CloudSyncRoot = CloudSyncRoot.Trim();
        settings.ObsidianVaultRoot = ObsidianVaultRoot.Trim();
        settings.LlmProvider    = LlmProvider.Trim();
        settings.LlmApiKey      = LlmApiKey.Trim();
        settings.LlmModel       = LlmModel.Trim();
        settings.LlmEndpoint    = LlmEndpoint.Trim();
        settings.LlmApiVersion  = LlmApiVersion.Trim();
        settings.LlmParameters  = ParseLlmParametersText(LlmParametersText);
        settings.LlmUserProfile = LlmUserProfile;
        settings.AiEnabled      = AiEnabled;
        settings.CaptureTaskLogEnabled = CaptureTaskLogEnabled;
        _configService.SaveSettings(settings);
        UpdateWorkspacePathsWarning();
    }

    [RelayCommand]
    public void SaveLlm()
    {
        var settings = _configService.LoadSettings();
        settings.LlmProvider    = LlmProvider.Trim();
        settings.LlmApiKey      = LlmApiKey.Trim();
        settings.LlmModel       = LlmModel.Trim();
        settings.LlmEndpoint    = LlmEndpoint.Trim();
        settings.LlmApiVersion  = LlmApiVersion.Trim();
        settings.LlmParameters  = ParseLlmParametersText(LlmParametersText);
        settings.LlmUserProfile = LlmUserProfile;
        settings.AiEnabled      = AiEnabled;
        _configService.SaveSettings(settings);
        LlmStatus = $"Saved {DateTime.Now:HH:mm:ss}";
    }

    [RelayCommand]
    public async Task TestLlmConnectionAsync()
    {
        LlmStatus = "Testing...";
        try
        {
            var reply = await _llmClientService.TestConnectionAsync(CancellationToken.None);
            LlmStatus = $"OK: {reply.Trim()} ({DateTime.Now:HH:mm:ss})";
            AiToggleCanEnable = true;
        }
        catch (Exception ex)
        {
            LlmStatus = $"Error: {ex.Message}";
            AiToggleCanEnable = false;
            AiEnabled = false;
        }
    }

    partial void OnAiEnabledChanged(bool value)
    {
        if (_loading) return;
        var settings = _configService.LoadSettings();
        settings.AiEnabled = value;
        _configService.SaveSettings(settings);
        WeakReferenceMessenger.Default.Send(new AiEnabledChangedMessage(value));
    }

    partial void OnLlmProviderChanged(string value)
    {
        if (_loading) return;
        LlmIsAzure = value.Equals("azure_openai", StringComparison.OrdinalIgnoreCase);
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

    partial void OnLocalProjectsRootChanged(string value)
    {
        if (_loading) return;
        UpdateWorkspacePathsWarning();
    }

    partial void OnCloudSyncRootChanged(string value)
    {
        if (_loading) return;
        UpdateWorkspacePathsWarning();
    }

    partial void OnObsidianVaultRootChanged(string value)
    {
        if (_loading) return;
        UpdateWorkspacePathsWarning();
    }

    private static Dictionary<string, string> ParseLlmParametersText(string text)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = line.IndexOf('=');
            if (idx <= 0) continue;
            var key = line[..idx].Trim();
            var val = line[(idx + 1)..].Trim();
            if (key.Length > 0)
                dict[key] = val;
        }
        return dict;
    }

    private void UpdateWorkspacePathsWarning()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(LocalProjectsRoot)) missing.Add("Local Projects Root");
        if (string.IsNullOrWhiteSpace(CloudSyncRoot)) missing.Add("Cloud Sync Root");
        if (string.IsNullOrWhiteSpace(ObsidianVaultRoot)) missing.Add("Obsidian Vault Root");

        WorkspacePathsWarning = missing.Count == 0
            ? ""
            : $"Missing required paths: {string.Join(", ", missing)}. Some features may not work until saved.";
    }
}
