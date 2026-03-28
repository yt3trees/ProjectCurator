using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using ProjectCurator.Interfaces;
using ProjectCurator.Models;
using ProjectCurator.Services;

namespace ProjectCurator.ViewModels;

// アクティビティバーの1日分
public class ActivityDay
{
    public bool IsActive { get; init; }
}

// Workstream カード行のモデル
public class WorkstreamCardItem
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string? FocusFile { get; set; }
    public string FocusFreshness { get; set; } = "missing";
    public string FocusAgeText { get; set; } = "–";
    public int DecisionLogCount { get; set; }
    public bool IsClosed { get; set; }
}

// プロジェクトカードのViewModel
public partial class ProjectCardViewModel : ObservableObject
{
    [ObservableProperty]
    private bool isHidden;

    public ProjectInfo Info { get; }

    public string DisplayName => Info.DisplayName;
    public string Tier => Info.Tier;
    public string Category => Info.Category;
    public int? FocusAge => Info.FocusAge;
    public int? SummaryAge => Info.SummaryAge;
    public string JunctionShared => Info.JunctionShared;
    public string JunctionObsidian => Info.JunctionObsidian;
    public string JunctionContext => Info.JunctionContext;
    public int DecisionLogCount => Info.DecisionLogCount;
    public bool HasUncommittedChanges => Info.HasUncommittedChanges;
    public int UncommittedRepoCount => Info.UncommittedRepoPaths.Count;
    public string UncommittedBadgeText => $"Uncommitted {UncommittedRepoCount}";
    public string UncommittedChangesTooltip => HasUncommittedChanges
        ? $"Uncommitted changes detected:\n{string.Join("\n", Info.UncommittedRepoPaths.Select(p => $"- {p}"))}"
        : "No uncommitted changes.";
    public IReadOnlyList<ActivityDay> ActivityDays { get; }

    public string FocusAgeText => FocusAge.HasValue ? $"{FocusAge}d" : "–";
    public string SummaryAgeText => SummaryAge.HasValue ? $"{SummaryAge}d" : "–";

    // "fresh" | "ok" | "aging" | "stale" | "missing"
    public string FocusFreshness => GetFreshness(FocusAge);
    public string SummaryFreshness => GetFreshness(SummaryAge);

    // ---- Workstream ----
    public ObservableCollection<WorkstreamCardItem> Workstreams { get; } = [];
    private List<WorkstreamCardItem> _allWorkstreams = [];
    public bool HasWorkstreams => _allWorkstreams.Count > 0;
    public bool HasClosedWorkstreams => _allWorkstreams.Any(w => w.IsClosed);
    public int ActiveWorkstreamCount => _allWorkstreams.Count(w => !w.IsClosed);

    [ObservableProperty]
    private bool isWorkstreamExpanded = false;

    [ObservableProperty]
    private bool showClosedWorkstreams = false;

    partial void OnShowClosedWorkstreamsChanged(bool value) => RebuildVisibleWorkstreams();

    private void RebuildVisibleWorkstreams()
    {
        Workstreams.Clear();
        var src = ShowClosedWorkstreams ? _allWorkstreams : _allWorkstreams.Where(w => !w.IsClosed);
        foreach (var w in src) Workstreams.Add(w);
    }

    public ProjectCardViewModel(ProjectInfo info)
    {
        Info = info;
        var today = DateTime.Today;
        var activeDates = info.FocusHistoryDates
            .Concat(info.DecisionLogDates)
            .Select(d => d.Date)
            .ToHashSet();
        var days = new List<ActivityDay>(30);
        for (int i = 29; i >= 0; i--)
            days.Add(new ActivityDay { IsActive = activeDates.Contains(today.AddDays(-i)) });
        ActivityDays = days;

        // Workstream カードを構築
        _allWorkstreams = info.Workstreams.Select(ws => new WorkstreamCardItem
        {
            Id = ws.Id,
            Label = ws.Label,
            FocusFile = ws.FocusFile,
            FocusFreshness = GetFreshness(ws.FocusAge),
            FocusAgeText = ws.FocusAge.HasValue ? $"{ws.FocusAge}d" : "–",
            DecisionLogCount = ws.DecisionLogCount,
            IsClosed = ws.IsClosed,
        }).ToList();

        // 初期表示はアクティブのみ
        foreach (var w in _allWorkstreams.Where(w => !w.IsClosed)) Workstreams.Add(w);
    }

    private static string GetFreshness(int? age) => age switch
    {
        null => "missing",
        <= 3 => "fresh",
        <= 7 => "ok",
        <= 14 => "aging",
        <= 30 => "stale",
        _ => "missing"
    };
}

public partial class DashboardViewModel : ObservableObject
{
    private readonly ProjectDiscoveryService _discoveryService;
    private readonly ConfigService _configService;
    private readonly TodayQueueService _todayQueueService;
    private readonly StateSnapshotService _stateSnapshotService;
    private readonly IDispatcherService _dispatcher;
    private readonly IShellService _shellService;
    private readonly IDialogService _dialogService;
    private System.Timers.Timer? _refreshTimer;
    private List<string> _hiddenKeys = [];
    private List<ProjectCardViewModel> _allCards = [];

    [ObservableProperty]
    private ObservableCollection<ProjectCardViewModel> projects = [];

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private ObservableCollection<TodayQueueTask> todayQueueTasks = [];

    [ObservableProperty]
    private string todayQueueStatus = "Today Queue: -";

    [ObservableProperty]
    private bool showAllTasks = false;

    [ObservableProperty]
    private ObservableCollection<string> projectFilterItems = ["All Projects"];

    [ObservableProperty]
    private string selectedProjectFilter = "All Projects";

    [ObservableProperty]
    private ObservableCollection<string> workstreamFilterItems = ["All Workstreams"];

    [ObservableProperty]
    private string selectedWorkstreamFilter = "All Workstreams";

    private List<TodayQueueTask> _cachedAllTasks = [];

    [ObservableProperty]
    private bool showHidden = false;

    // ---------- Pinned Folders ----------

    private List<PinnedFolder> _pinnedFoldersList = [];

    [ObservableProperty]
    private ObservableCollection<PinnedFolder> pinnedFolders = [];

    public bool HasPinnedFolders => PinnedFolders.Count > 0;

    partial void OnShowHiddenChanged(bool value) => ApplyFilter();

    public int HiddenCount => _allCards.Count(c => c.IsHidden);

    [ObservableProperty]
    private int snoozeCount;

    // Snooze カウントが 0 より大きい場合のみ表示用
    public bool HasSnoozed => SnoozeCount > 0;

    [ObservableProperty]
    private bool isAiEnabled;

    public int AutoRefreshMinutes { get; set; }
    public int TodayQueueLimit { get; set; }

    // Dashboard -> Editor 遷移コールバック
    public Action<ProjectInfo>? OnOpenInEditor;

    // Dashboard -> Timeline 遷移コールバック
    public Action<ProjectInfo>? OnOpenInTimeline;

    public DashboardViewModel(
        ProjectDiscoveryService discoveryService,
        ConfigService configService,
        TodayQueueService todayQueueService,
        StateSnapshotService stateSnapshotService,
        IDispatcherService dispatcher,
        IShellService shellService,
        IDialogService dialogService)
    {
        _discoveryService = discoveryService;
        _configService = configService;
        _todayQueueService = todayQueueService;
        _stateSnapshotService = stateSnapshotService;
        _dispatcher = dispatcher;
        _shellService = shellService;
        _dialogService = dialogService;
        var settings = configService.LoadSettings();
        AutoRefreshMinutes = settings.DashboardAutoRefreshMinutes;
        TodayQueueLimit = settings.DashboardTodayQueueLimit;
        _hiddenKeys = configService.LoadHiddenProjects();

        _pinnedFoldersList = configService.LoadPinnedFolders();
        foreach (var pf in _pinnedFoldersList) PinnedFolders.Add(pf);
        PinnedFolders.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasPinnedFolders));

        IsAiEnabled = _configService.LoadSettings().AiEnabled;
        WeakReferenceMessenger.Default.Register<AiEnabledChangedMessage>(this,
            (_, msg) => IsAiEnabled = msg.Enabled);
    }

    public List<TodayQueueTask> GetTopTasksForAi(int limit = 30)
        => _cachedAllTasks.Take(limit).ToList();

    public async Task RefreshAsync(bool force = false)
    {
        IsLoading = true;
        try
        {
            var list = await _discoveryService.GetProjectInfoListAsync(force: force);
            _allCards = list.Select(p => new ProjectCardViewModel(p)
            {
                IsHidden = _hiddenKeys.Contains(p.HiddenKey)
            }).ToList();
            ApplyFilter();
            // Today Queue もキャッシュ済みリストで更新
            await LoadTodayQueueInternalAsync(list);
            // State Snapshot をバックグラウンドで書き出す
            _ = _stateSnapshotService.ExportAsync(list);
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task LoadTodayQueueAsync()
    {
        TodayQueueStatus = "Today Queue: Loading...";
        try
        {
            var list = await _discoveryService.GetProjectInfoListAsync();
            await LoadTodayQueueInternalAsync(list);
        }
        catch (Exception ex)
        {
            TodayQueueStatus = $"Today Queue: Error - {ex.Message}";
        }
    }

    private async Task LoadTodayQueueInternalAsync(List<ProjectInfo> projects)
    {
        try
        {
            TodayQueueLimit = _configService.LoadSettings().DashboardTodayQueueLimit;
            var allTasks = await Task.Run(() => _todayQueueService.GetAllTasksSorted(projects, 10000));
            _todayQueueService.EnsureSnoozeLoaded();
            _cachedAllTasks = allTasks;

            // フィルタ一覧を更新 (現在の選択値を保持)
            var projectNames = allTasks
                .Select(t => t.ProjectShortName)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct()
                .OrderBy(n => n)
                .ToList();

            var workstreamNames = allTasks
                .Where(t => t.HasWorkstream)
                .Select(t => t.WorkstreamLabel!)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct()
                .OrderBy(n => n)
                .ToList();

            _dispatcher.Post(() =>
            {
                var currentProject = SelectedProjectFilter;
                var currentWorkstream = SelectedWorkstreamFilter;
                ProjectFilterItems.Clear();
                ProjectFilterItems.Add("All Projects");
                foreach (var name in projectNames)
                    if (!ProjectFilterItems.Contains(name))
                        ProjectFilterItems.Add(name);

                WorkstreamFilterItems.Clear();
                WorkstreamFilterItems.Add("All Workstreams");
                foreach (var name in workstreamNames)
                    if (!WorkstreamFilterItems.Contains(name))
                        WorkstreamFilterItems.Add(name);

                SelectedProjectFilter = ProjectFilterItems.Contains(currentProject) ? currentProject : "All Projects";
                SelectedWorkstreamFilter = WorkstreamFilterItems.Contains(currentWorkstream) ? currentWorkstream : "All Workstreams";
            });

            ApplyProjectFilter();
        }
        catch (Exception ex)
        {
            _dispatcher.Post(() =>
                TodayQueueStatus = $"Today Queue: Error - {ex.Message}");
        }
    }

    partial void OnSelectedProjectFilterChanged(string value) => ApplyProjectFilter();
    partial void OnSelectedWorkstreamFilterChanged(string value) => ApplyProjectFilter();

    private void ApplyProjectFilter()
    {
        var tasks = _cachedAllTasks;
        var projectFilter = SelectedProjectFilter;
        var workstreamFilter = SelectedWorkstreamFilter;
        var isProjectFiltered = projectFilter != "All Projects" && !string.IsNullOrEmpty(projectFilter);
        var isWorkstreamFiltered = workstreamFilter != "All Workstreams" && !string.IsNullOrEmpty(workstreamFilter);

        if (isProjectFiltered)
        {
            var normalizedFilter = projectFilter.Trim();

            // "Project / Workstream" 指定なら完全一致。
            // "Project" 指定なら配下の "Project / xxx" も含める。
            if (normalizedFilter.Contains(" / ", StringComparison.Ordinal))
            {
                tasks = tasks.Where(t =>
                    string.Equals(t.ProjectFilterLabel.Trim(), normalizedFilter, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
            else
            {
                tasks = tasks.Where(t =>
                {
                    var project = t.ProjectShortName.Trim();
                    var composite = t.ProjectFilterLabel.Trim();
                    return string.Equals(project, normalizedFilter, StringComparison.OrdinalIgnoreCase)
                        || composite.StartsWith($"{normalizedFilter} / ", StringComparison.OrdinalIgnoreCase);
                }).ToList();
            }
        }

        if (isWorkstreamFiltered)
        {
            var normalizedWorkstream = workstreamFilter.Trim();
            tasks = tasks.Where(t =>
                !string.IsNullOrWhiteSpace(t.WorkstreamLabel) &&
                string.Equals(t.WorkstreamLabel.Trim(), normalizedWorkstream, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var limit = ShowAllTasks ? 100 : TodayQueueLimit;
        var visible = tasks.Where(t => !_todayQueueService.IsSnoozed(t.SnoozeKey)).Take(limit).ToList();
        var snoozed = tasks.Count(t => _todayQueueService.IsSnoozed(t.SnoozeKey));

        _dispatcher.Post(() =>
        {
            TodayQueueTasks.Clear();
            foreach (var t in visible) TodayQueueTasks.Add(t);
            SnoozeCount = snoozed;
            OnPropertyChanged(nameof(HasSnoozed));

            string status;
            if (visible.Count == 0 && snoozed == 0)
                status = "Today Queue: No tasks";
            else if (visible.Count == 0)
                status = $"Today Queue: {snoozed} snoozed";
            else
            {
                var suffixParts = new List<string>();
                if (isProjectFiltered) suffixParts.Add(projectFilter);
                if (isWorkstreamFiltered) suffixParts.Add(workstreamFilter);
                var suffix = suffixParts.Count > 0
                    ? $" ({string.Join(" / ", suffixParts)})"
                    : (ShowAllTasks ? "" : $" (Top {TodayQueueLimit})");
                status = $"Today Queue: {visible.Count} items{suffix}";
                if (snoozed > 0) status += $", {snoozed} snoozed";
            }
            TodayQueueStatus = status;
        });
    }

    public async Task CompleteTaskAsync(TodayQueueTask task)
    {
        TodayQueueStatus = "Today Queue: Submitting to Asana...";
        var (ok, msg) = await _todayQueueService.CompleteAsanaTaskAsync(task.AsanaTaskGid!);
        if (ok)
        {
            await Task.Run(() => _todayQueueService.MarkTaskCompletedInFile(task));
            _cachedAllTasks.Remove(task);
            _dispatcher.Post(() => TodayQueueTasks.Remove(task));
        }
        TodayQueueStatus = $"Today Queue: {msg}";
    }

    public async Task SnoozeTaskAsync(TodayQueueTask task)
    {
        await Task.Run(() => _todayQueueService.SnoozeTask(task.SnoozeKey));
        ApplyProjectFilter();
    }

    public async Task UnsnoozeAllAsync()
    {
        await Task.Run(() => _todayQueueService.UnsnoozeAll());
        ApplyProjectFilter();
    }

    public void SetupAutoRefresh()
    {
        _refreshTimer?.Stop();
        _refreshTimer?.Dispose();
        _refreshTimer = null;
        if (AutoRefreshMinutes <= 0) return;
        _refreshTimer = new System.Timers.Timer(AutoRefreshMinutes * 60_000);
        _refreshTimer.Elapsed += async (_, _) => await RefreshAsync(force: true);
        _refreshTimer.AutoReset = true;
        _refreshTimer.Start();
    }

    private void ApplyFilter()
    {
        _dispatcher.Post(() =>
        {
            Projects.Clear();
            var src = ShowHidden ? _allCards : _allCards.Where(c => !c.IsHidden);
            foreach (var c in src) Projects.Add(c);
            OnPropertyChanged(nameof(HiddenCount));
        });
    }

    public void HideProject(ProjectCardViewModel card)
    {
        if (!_hiddenKeys.Contains(card.Info.HiddenKey))
        {
            _hiddenKeys.Add(card.Info.HiddenKey);
            _configService.SaveHiddenProjects(_hiddenKeys);
        }
        card.IsHidden = true;
        if (!ShowHidden)
            Projects.Remove(card);
        OnPropertyChanged(nameof(HiddenCount));
    }

    public void UnhideProject(ProjectCardViewModel card)
    {
        _hiddenKeys.Remove(card.Info.HiddenKey);
        _configService.SaveHiddenProjects(_hiddenKeys);
        card.IsHidden = false;
        ApplyFilter();
    }

    public void OpenDirectory(ProjectCardViewModel card)
    {
        if (Directory.Exists(card.Info.Path))
            _shellService.OpenFolder(card.Info.Path);
    }

    public void OpenTerminal(ProjectCardViewModel card)
    {
        if (Directory.Exists(card.Info.Path))
            _shellService.OpenTerminal(card.Info.Path);
    }

    // ---------- Pinned Folders: ピン操作 ----------

    public void PinFolder(ProjectCardViewModel card, WorkstreamCardItem? workstream, string folderName, string fullPath)
    {
        // 重複チェック
        if (_pinnedFoldersList.Any(p =>
            p.Project == card.Info.Name &&
            p.Workstream == workstream?.Id &&
            p.Folder == folderName))
            return;

        var pf = new PinnedFolder
        {
            Project = card.Info.Name,
            Workstream = workstream?.Id,
            Folder = folderName,
            FullPath = fullPath,
            PinnedAt = DateTime.Today.ToString("yyyy-MM-dd"),
        };
        _pinnedFoldersList.Add(pf);
        _configService.SavePinnedFolders(_pinnedFoldersList);
        _dispatcher.Post(() => PinnedFolders.Add(pf));
    }

    public void MovePinnedFolder(PinnedFolder source, PinnedFolder target)
    {
        var fromIndex = _pinnedFoldersList.IndexOf(source);
        var toIndex = _pinnedFoldersList.IndexOf(target);
        if (fromIndex < 0 || toIndex < 0 || fromIndex == toIndex) return;
        _pinnedFoldersList.RemoveAt(fromIndex);
        _pinnedFoldersList.Insert(toIndex, source);
        _configService.SavePinnedFolders(_pinnedFoldersList);
        PinnedFolders.Move(fromIndex, toIndex);
    }

    public void UnpinFolder(PinnedFolder pf)
    {
        _pinnedFoldersList.Remove(pf);
        _configService.SavePinnedFolders(_pinnedFoldersList);
        PinnedFolders.Remove(pf);
    }

    public void ClearPinnedFolders()
    {
        _pinnedFoldersList.Clear();
        _configService.SavePinnedFolders(_pinnedFoldersList);
        PinnedFolders.Clear();
    }

    public async Task OpenPinnedFolderAsync(PinnedFolder pf)
    {
        if (Directory.Exists(pf.FullPath))
            _shellService.OpenFolder(pf.FullPath);
        else
            await _dialogService.ShowMessageAsync("Folder Not Found", "The folder no longer exists.");
    }

    // Keep synchronous overload for backward-compat callers that don't need async
    public void OpenPinnedFolder(PinnedFolder pf)
    {
        if (Directory.Exists(pf.FullPath))
            _shellService.OpenFolder(pf.FullPath);
        else
            _ = _dialogService.ShowMessageAsync("Folder Not Found", "The folder no longer exists.");
    }

    public async Task<List<(string FolderName, string FullPath)>> GetRecentWorkFoldersAsync(string projectPath, string? workstreamId, int limit = 10)
    {
        return await Task.Run(() =>
        {
            var results = new List<(string, string)>();
            try
            {
                bool isWorkstream = !string.IsNullOrWhiteSpace(workstreamId);
                IEnumerable<string> yearMonthDirs;

                if (isWorkstream)
                {
                    var baseRoot = Path.Combine(projectPath, "shared", "_work", workstreamId!);
                    if (!Directory.Exists(baseRoot)) return results;

                    yearMonthDirs = Directory.GetDirectories(baseRoot)
                        .Where(d => Regex.IsMatch(Path.GetFileName(d), @"^\d{6}$"))
                        .OrderByDescending(d => d);
                }
                else
                {
                    var baseRoot = Path.Combine(projectPath, "shared", "_work");
                    if (!Directory.Exists(baseRoot)) return results;

                    // year/yearMonth 構造をフラット化して降順に並べる
                    var allYM = new List<string>();
                    foreach (var yearDir in Directory.GetDirectories(baseRoot)
                        .Where(d => Regex.IsMatch(Path.GetFileName(d), @"^\d{4}$"))
                        .OrderByDescending(d => d))
                    {
                        allYM.AddRange(
                            Directory.GetDirectories(yearDir)
                                .Where(d => Regex.IsMatch(Path.GetFileName(d), @"^\d{6}$"))
                                .OrderByDescending(d => d));
                    }
                    yearMonthDirs = allYM;
                }

                foreach (var ymDir in yearMonthDirs)
                {
                    if (results.Count >= limit) break;
                    foreach (var workDir in Directory.GetDirectories(ymDir)
                        .Where(d => Regex.IsMatch(Path.GetFileName(d), @"^\d{8}_"))
                        .OrderByDescending(d => d))
                    {
                        results.Add((Path.GetFileName(workDir), workDir));
                        if (results.Count >= limit) break;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Dashboard] GetRecentWorkFolders error: {ex}");
            }
            return results;
        });
    }

    public void OpenVSCode(ProjectCardViewModel card)
    {
        if (!Directory.Exists(card.Info.Path)) return;
        _shellService.OpenFile($"code \"{card.Info.Path}\"");
    }

    public void OpenWorkRoot(ProjectCardViewModel card)
    {
        var workRoot = Path.Combine(card.Info.Path, "shared", "_work");
        if (Directory.Exists(workRoot))
            _shellService.OpenFolder(workRoot);
        else
            _ = _dialogService.ShowMessageAsync("Folder Not Found", "The shared\\_work folder does not exist.");
    }

    public void OpenWorkstreamWorkRoot(ProjectCardViewModel card, WorkstreamCardItem workstream)
    {
        var workstreamRoot = Path.Combine(card.Info.Path, "shared", "_work", workstream.Id);
        if (Directory.Exists(workstreamRoot))
            _shellService.OpenFolder(workstreamRoot);
        else
            _ = _dialogService.ShowMessageAsync("Folder Not Found", "The workstream work root folder does not exist.");
    }

    public string? CreateTodayGeneralWorkFolder(ProjectCardViewModel card, string featureName)
    {
        return CreateTodayWorkFolder(card.Info.Path, workstreamId: null, featureName);
    }

    public string? CreateTodayWorkstreamWorkFolder(ProjectCardViewModel card, WorkstreamCardItem workstream, string featureName)
    {
        return CreateTodayWorkFolder(card.Info.Path, workstream.Id, featureName);
    }

    public void OpenAgentTerminal(ProjectCardViewModel card, string agent)
    {
        if (!Directory.Exists(card.Info.Path)) return;
        OpenAgentTerminalAtPath(card.Info.Path, agent);
    }

    private string? CreateTodayWorkFolder(string projectPath, string? workstreamId, string featureName)
    {
        try
        {
            var today = DateTime.Today;
            var year = today.ToString("yyyy");
            var yearMonth = today.ToString("yyyyMM");
            var day = today.ToString("yyyyMMdd");
            var safeFeature = NormalizeWorkFolderSuffix(featureName);
            if (string.IsNullOrWhiteSpace(safeFeature))
            {
                _ = _dialogService.ShowMessageAsync("Invalid Name", "Feature name is required.");
                return null;
            }

            var baseRoot = string.IsNullOrWhiteSpace(workstreamId)
                ? Path.Combine(projectPath, "shared", "_work")
                : Path.Combine(projectPath, "shared", "_work", workstreamId);

            var target = string.IsNullOrWhiteSpace(workstreamId)
                ? Path.Combine(baseRoot, year, yearMonth, $"{day}_{safeFeature}")
                : Path.Combine(baseRoot, yearMonth, $"{day}_{safeFeature}");
            Directory.CreateDirectory(target);
            return target;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Dashboard] Failed to create today work folder: {ex}");
            _ = _dialogService.ShowMessageAsync("Create Folder Failed", $"Failed to create work folder.\n{ex.Message}");
            return null;
        }
    }

    private static string NormalizeWorkFolderSuffix(string value)
    {
        var trimmed = value.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return "";
        return Regex.Replace(trimmed, @"[\\/:*?""<>|]", "_");
    }

    public void OpenAgentTerminalAtPath(string path, string agent)
    {
        // TODO: Phase 1 - extend IShellService with agent terminal support
        _shellService.OpenTerminal(path);
    }

    public void OpenInEditor(ProjectCardViewModel card) => OnOpenInEditor?.Invoke(card.Info);

    public void OpenInTimeline(ProjectCardViewModel card) => OnOpenInTimeline?.Invoke(card.Info);

    [RelayCommand]
    private void ToggleShowAll()
    {
        ShowAllTasks = !ShowAllTasks;
        ApplyProjectFilter();
    }
}
