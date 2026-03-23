using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectCurator.Models;
using ProjectCurator.Services;

namespace ProjectCurator.ViewModels;

internal class AsanaProjectConfig
{
    [JsonPropertyName("asana_project_gids")]
    public List<string> AsanaProjectGids { get; set; } = [];

    [JsonPropertyName("anken_aliases")]
    public List<string> AnkenAliases { get; set; } = [];

    [JsonPropertyName("workstream_project_map")]
    public Dictionary<string, string> WorkstreamProjectMap { get; set; } = [];

    [JsonPropertyName("workstream_field_name")]
    public string WorkstreamFieldName { get; set; } = "workstream-id";
}

public partial class AsanaSyncViewModel : ObservableObject
{
    private readonly ConfigService _configService;
    private readonly ProjectDiscoveryService _discoveryService;
    private readonly AsanaSyncService _asanaSyncService;
    private System.Timers.Timer? _syncTimer;
    private bool _initialized;

    [ObservableProperty]
    private ObservableCollection<ProjectInfo> projects = [];

    [ObservableProperty]
    private ProjectInfo? selectedProject;

    [ObservableProperty]
    private bool isSyncing;

    [ObservableProperty]
    private bool scheduleEnabled;

    [ObservableProperty]
    private int intervalMin = 60;

    [ObservableProperty]
    private string lastSyncTime = "--";

    [ObservableProperty]
    private string outputLog = "";

    [ObservableProperty]
    private string asanaConfigGids = "";

    [ObservableProperty]
    private string asanaConfigAliases = "";

    [ObservableProperty]
    private string asanaConfigWorkstreamMap = "";

    [ObservableProperty]
    private string asanaConfigWorkstreamFieldName = "workstream-id";

    [ObservableProperty]
    private string asanaConfigStatus = "";

    public AsanaSyncViewModel(
        ConfigService configService,
        ProjectDiscoveryService discoveryService,
        AsanaSyncService asanaSyncService)
    {
        _configService = configService;
        _discoveryService = discoveryService;
        _asanaSyncService = asanaSyncService;
    }

    public void StartScheduler()
    {
        var settings = _configService.LoadSettings();
        if (settings.AsanaSync != null)
        {
            IntervalMin = settings.AsanaSync.IntervalMin > 0 ? settings.AsanaSync.IntervalMin : 60;
            ScheduleEnabled = settings.AsanaSync.Enabled;  // OnScheduleEnabledChanged でタイマー開始
        }
    }

    public async Task InitAsync()
    {
        if (_initialized) return;
        _initialized = true;

        // プロジェクトリスト読み込み
        var infos = await Task.Run(() => _discoveryService.GetProjectInfoList());
        Projects.Clear();
        foreach (var p in infos) Projects.Add(p);

        // スケジュール設定は StartScheduler() で読み込み済み
    }

    [RelayCommand]
    private async Task Sync() => await RunSyncAsync();

    [RelayCommand]
    private void ClearOutput() => OutputLog = "";

    [RelayCommand]
    private void SaveSchedule()
    {
        var settings = _configService.LoadSettings();
        settings.AsanaSync ??= new AsanaSyncConfig();
        settings.AsanaSync.Enabled = ScheduleEnabled;
        settings.AsanaSync.IntervalMin = IntervalMin;
        _configService.SaveSettings(settings);

        UpdateScheduleTimer();

        var stateText = ScheduleEnabled ? "ON" : "OFF";
        AppendOutput($"[SAVED] Schedule: {stateText}, Interval: {IntervalMin} min\n");
    }

    partial void OnScheduleEnabledChanged(bool value) => UpdateScheduleTimer();

    [RelayCommand]
    private void LoadAsanaConfig()
    {
        if (SelectedProject == null)
        {
            AsanaConfigStatus = "Please select a project";
            return;
        }

        var configPath = ResolveAsanaConfigPath(SelectedProject);
        if (!File.Exists(configPath))
        {
            AsanaConfigGids = "";
            AsanaConfigAliases = "";
            AsanaConfigWorkstreamMap = "";
            AsanaConfigWorkstreamFieldName = "workstream-id";
            AsanaConfigStatus = "New file (not yet created)";
            return;
        }

        try
        {
            var content = File.ReadAllText(configPath, new UTF8Encoding(false));
            var config = JsonSerializer.Deserialize<AsanaProjectConfig>(content) ?? new AsanaProjectConfig();
            AsanaConfigGids = string.Join("\n", config.AsanaProjectGids);
            AsanaConfigAliases = string.Join("\n", config.AnkenAliases);
            AsanaConfigWorkstreamMap = string.Join("\n",
                config.WorkstreamProjectMap
                    .OrderBy(kv => kv.Key)
                    .Select(kv => $"{kv.Key}={kv.Value}"));
            AsanaConfigWorkstreamFieldName = string.IsNullOrWhiteSpace(config.WorkstreamFieldName)
                ? "workstream-id"
                : config.WorkstreamFieldName.Trim();
            AsanaConfigStatus = "Loaded";
        }
        catch (Exception ex)
        {
            AsanaConfigStatus = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private void SaveAsanaConfig()
    {
        if (SelectedProject == null)
        {
            AsanaConfigStatus = "Please select a project";
            return;
        }

        var configPath = ResolveAsanaConfigPath(SelectedProject);
        try
        {
            var dir = Path.GetDirectoryName(configPath)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            var gids = AsanaConfigGids
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
            var aliases = AsanaConfigAliases
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

            var map = AsanaConfigWorkstreamMap
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .Select(ParseWorkstreamMapLine)
                .Where(x => x != null)
                .GroupBy(x => x!.Value.ProjectGid, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Last()!.Value.WorkstreamId, StringComparer.OrdinalIgnoreCase);

            var fieldName = string.IsNullOrWhiteSpace(AsanaConfigWorkstreamFieldName)
                ? "workstream-id"
                : AsanaConfigWorkstreamFieldName.Trim();

            var config = new AsanaProjectConfig
            {
                AsanaProjectGids = gids,
                AnkenAliases = aliases,
                WorkstreamProjectMap = map,
                WorkstreamFieldName = fieldName
            };
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(configPath, json, new UTF8Encoding(false));
            AsanaConfigStatus = $"Saved {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            AsanaConfigStatus = $"Error: {ex.Message}";
        }
    }

    // ----- プライベート -----

    private async Task RunSyncAsync()
    {
        if (IsSyncing) return;

        IsSyncing = true;
        AppendOutput($">>> Asana Sync (C#)\n---\n");

        try
        {
            await Task.Run(async () => await _asanaSyncService.RunAsync(AppendOutput));
            AppendOutput("\n--- Done (exit: 0) ---\n");
            LastSyncTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }
        catch (Exception ex)
        {
            AppendOutput($"\n[ERROR] {ex.Message}\n--- Done (error) ---\n");
        }
        finally
        {
            IsSyncing = false;
        }
    }

    private void UpdateScheduleTimer()
    {
        _syncTimer?.Stop();
        _syncTimer?.Dispose();
        _syncTimer = null;

        if (!ScheduleEnabled || IntervalMin <= 0) return;

        _syncTimer = new System.Timers.Timer(IntervalMin * 60_000.0);
        _syncTimer.Elapsed += async (_, _) =>
        {
            AppendOutput("\n=== Scheduled Sync ===\n");
            await RunSyncAsync();
        };
        _syncTimer.AutoReset = true;
        _syncTimer.Start();
    }

    private void AppendOutput(string text)
    {
        Application.Current.Dispatcher.InvokeAsync(() => OutputLog += text);
    }

    private static (string ProjectGid, string WorkstreamId)? ParseWorkstreamMapLine(string line)
    {
        var raw = line.Trim();
        if (raw.Length == 0 || raw.StartsWith('#')) return null;

        // 受け付け形式:
        // 1) gid=ws
        // 2) gid:ws
        // 3) gid -> ws
        // 4) gid,ws
        // 5) gid ws
        foreach (var sep in new[] { "->", "=", ":", "," })
        {
            var idx = raw.IndexOf(sep, StringComparison.Ordinal);
            if (idx <= 0 || idx + sep.Length >= raw.Length) continue;

            var gid = raw[..idx].Trim();
            var ws = raw[(idx + sep.Length)..].Trim();
            if (gid.Length > 0 && ws.Length > 0)
                return (gid, ws);
        }

        var parts = raw.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
            return (parts[0], parts[1]);

        return null;
    }

    private string ResolveAsanaConfigPath(ProjectInfo project)
    {
        var paths = _configService.LoadSettings();
        var boxRoot = paths.BoxProjectsRoot.TrimEnd('\\', '/');

        return project.Category == "domain"
            ? project.Tier == "mini"
                ? Path.Combine(boxRoot, "_domains", "_mini", project.Name, "asana_config.json")
                : Path.Combine(boxRoot, "_domains", project.Name, "asana_config.json")
            : project.Tier == "mini"
                ? Path.Combine(boxRoot, "_mini", project.Name, "asana_config.json")
                : Path.Combine(boxRoot, project.Name, "asana_config.json");
    }
}
