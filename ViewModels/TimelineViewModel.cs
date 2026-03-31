using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectCurator.Helpers;
using ProjectCurator.Models;
using ProjectCurator.Services;

namespace ProjectCurator.ViewModels;

/// <summary>タイムラインの1エントリ (通常行 or ギャップ行)</summary>
public class TimelineEntryItem
{
    public bool IsGap { get; init; }
    public int GapDays { get; init; }           // IsGap == true のとき有効
    public DateTime Date { get; init; }          // IsGap == false のとき有効
    public string EntryType { get; init; } = ""; // "Focus" | "Decision"
    public string Preview { get; init; } = "";
    public string FilePath { get; init; } = "";
    public string ProjectName { get; init; } = "";
    public string ProjectHiddenKey { get; init; } = "";

    public string GapText => GapDays == 1 ? "-- 1 day gap --" : $"-- {GapDays} days gap --";
    public string DateText => Date.ToString("yyyy-MM-dd (ddd)");
    public string TypeBadge => EntryType switch
    {
        "Decision" => "[Decision]",
        "Work" => "[Work]",
        _ => "[Focus]",
    };
}

public class TimelineHeatmapCellItem
{
    public string ProjectHiddenKey { get; init; } = "";
    public string BucketLabel { get; init; } = "";
    public DateTime BucketDate { get; init; }
    public int Count { get; init; }
    public double Intensity { get; init; }
    public string ToolTipText { get; init; } = "";
}

public class TimelineHeatmapBucketItem
{
    public DateTime Date { get; init; }
    public string Label { get; init; } = "";
    public string ShortLabel { get; init; } = "";
}

public class TimelineHeatmapRowItem
{
    public string ProjectName { get; init; } = "";
    public string ProjectKey { get; init; } = "";
    public ObservableCollection<TimelineHeatmapCellItem> Cells { get; } = [];
}

public partial class TimelineViewModel : ObservableObject
{
    private readonly ProjectDiscoveryService _discoveryService;
    private readonly ConfigService _configService;
    private List<ProjectInfo> _allProjects = [];
    private List<string> _hiddenKeys = [];

    [ObservableProperty]
    private ObservableCollection<ProjectInfo> projects = [];

    [ObservableProperty]
    private bool showHidden = false;

    public int HiddenCount => _allProjects.Count(p => _hiddenKeys.Contains(p.HiddenKey));

    [ObservableProperty]
    private ProjectInfo? selectedProject;

    [ObservableProperty]
    private int daysBack = 30;  // 0 = All

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private bool isGraphLoading;

    [ObservableProperty]
    private string statsText = "No project selected";

    [ObservableProperty]
    private string graphStatsText = "";

    [ObservableProperty]
    private string graphEmptyText = "No timeline events found for this range.";

    [ObservableProperty]
    private string selectedGraphScope = "All projects";

    public ObservableCollection<TimelineEntryItem> Entries { get; } = [];
    public ObservableCollection<TimelineHeatmapBucketItem> HeatmapBuckets { get; } = [];
    public ObservableCollection<TimelineHeatmapRowItem> HeatmapRows { get; } = [];
    public ObservableCollection<string> GraphScopes { get; } = ["All projects", "Selected project"];

    /// <summary>エントリクリック時のコールバック (ProjectInfo, filePath)</summary>
    public Action<ProjectInfo, string>? OnOpenFileInEditor;

    /// <summary>Dashboard からジャンプ時のターゲットキー (InitAsync後にSelectedProjectへ反映)</summary>
    public string? NavigateToProjectKey { get; set; }

    public TimelineViewModel(ProjectDiscoveryService discoveryService, ConfigService configService)
    {
        _discoveryService = discoveryService;
        _configService = configService;
    }

    /// <summary>ページ表示時に呼ぶ。プロジェクトリストを読み込む。</summary>
    public async Task InitAsync()
    {
        IsLoading = true;
        try
        {
            var infos = await Task.Run(() => _discoveryService.GetProjectInfoList());
            _hiddenKeys = _configService.LoadHiddenProjects();
            _allProjects = infos;
            ApplyProjectFilter();
            OnPropertyChanged(nameof(HiddenCount));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Timeline] InitAsync failed: {ex}");
        }
        finally
        {
            IsLoading = false;
        }

        await LoadHeatmapAsync();
    }

    partial void OnShowHiddenChanged(bool value)
    {
        ApplyProjectFilter();
        _ = LoadHeatmapAsync();
    }

    private void ApplyProjectFilter()
    {
        var current = SelectedProject?.HiddenKey;
        var src = ShowHidden ? _allProjects : _allProjects.Where(p => !_hiddenKeys.Contains(p.HiddenKey));
        Projects.Clear();
        foreach (var p in src) Projects.Add(p);
        // 選択中プロジェクトがフィルタで消えた場合は選択解除
        if (current != null && !Projects.Any(p => p.HiddenKey == current))
            SelectedProject = null;
    }

    partial void OnSelectedProjectChanged(ProjectInfo? value)
    {
        _ = LoadEntriesAsync();
        _ = LoadHeatmapAsync();
    }

    partial void OnDaysBackChanged(int value)
    {
        _ = LoadEntriesAsync();
        _ = LoadHeatmapAsync();
    }

    partial void OnSelectedGraphScopeChanged(string value)
        => _ = LoadHeatmapAsync();

    /// <summary>タイムラインエントリを非同期で読み込む。</summary>
    public async Task LoadEntriesAsync()
    {
        Entries.Clear();

        if (SelectedProject == null)
        {
            StatsText = "No project selected";
            return;
        }

        StatsText = "Loading...";
        IsLoading = true;
        var project = SelectedProject;
        var daysBack = DaysBack;

        try
        {
            var (entries, stats) = await Task.Run(() => BuildEntries(project, daysBack));

            foreach (var e in entries)
                Entries.Add(e);
            StatsText = stats;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Timeline] LoadEntriesAsync failed: {ex}");
            StatsText = $"[ERROR] {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task LoadHeatmapAsync()
    {
        HeatmapBuckets.Clear();
        HeatmapRows.Clear();
        GraphStatsText = "";
        GraphEmptyText = "No timeline events found for this range.";

        if (Projects.Count == 0)
        {
            GraphEmptyText = "No projects available.";
            return;
        }

        IsGraphLoading = true;
        try
        {
            var result = await Task.Run(() => BuildHeatmap(
                [.. Projects],
                SelectedProject,
                DaysBack,
                SelectedGraphScope));

            foreach (var bucket in result.Buckets)
                HeatmapBuckets.Add(bucket);
            foreach (var row in result.Rows)
                HeatmapRows.Add(row);

            GraphStatsText = result.Stats;
            GraphEmptyText = result.EmptyText;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Timeline] LoadHeatmapAsync failed: {ex}");
            GraphEmptyText = $"Failed to load graph: {ex.Message}";
        }
        finally
        {
            IsGraphLoading = false;
        }
    }

    /// <summary>エントリクリック時に呼ぶ。</summary>
    public void OpenEntry(TimelineEntryItem entry)
    {
        if (entry.IsGap || string.IsNullOrEmpty(entry.FilePath))
            return;

        if (entry.EntryType == "Work")
        {
            if (Directory.Exists(entry.FilePath))
                Process.Start("explorer.exe", $"\"{entry.FilePath}\"");
            return;
        }

        var project = Projects.FirstOrDefault(p => p.HiddenKey == entry.ProjectHiddenKey) ?? SelectedProject;
        if (project == null)
            return;

        OnOpenFileInEditor?.Invoke(project, entry.FilePath);
    }

    public void OpenHeatmapCell(TimelineHeatmapCellItem? cell)
    {
        if (cell == null || cell.Count <= 0)
            return;

        var project = Projects.FirstOrDefault(p => p.HiddenKey == cell.ProjectHiddenKey);
        if (project == null)
            return;

        var focusPath = Path.Combine(project.AiContextContentPath, "focus_history", $"{cell.BucketDate:yyyy-MM-dd}.md");
        if (File.Exists(focusPath))
        {
            OnOpenFileInEditor?.Invoke(project, focusPath);
            return;
        }

        var decisionDir = Path.Combine(project.AiContextContentPath, "decision_log");
        if (!Directory.Exists(decisionDir))
            return;

        var pattern = $"{cell.BucketDate:yyyy-MM-dd}_*.md";
        var decisionPath = Directory.EnumerateFiles(decisionDir, pattern)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(decisionPath))
            OnOpenFileInEditor?.Invoke(project, decisionPath);
    }

    // ----- プライベート -----

    private static (List<TimelineEntryItem> entries, string stats) BuildEntries(ProjectInfo project, int daysBack)
    {
        var cutoff = daysBack > 0
            ? DateTime.Today.AddDays(-daysBack)
            : DateTime.MinValue;

        var rawEntries = BuildRawEntries(project, cutoff);

        if (rawEntries.Count == 0)
            return ([], "Total: 0 entries");

        // 降順ソート (新しい順)
        rawEntries.Sort((a, b) =>
        {
            int cmp = b.Date.CompareTo(a.Date);
            if (cmp != 0)
                return cmp;
            return string.Compare(a.Type, b.Type, StringComparison.Ordinal);
        });

        // エントリ + ギャップ構築
        var result = new List<TimelineEntryItem>();
        DateTime? prevDate = null;

        foreach (var raw in rawEntries)
        {
            // ギャップ挿入 (日付が変わっている場合)
            if (prevDate.HasValue && prevDate.Value.Date != raw.Date.Date)
            {
                int gap = (int)(prevDate.Value.Date - raw.Date.Date).TotalDays - 1;
                if (gap > 0)
                    result.Add(new TimelineEntryItem { IsGap = true, GapDays = gap });
            }

            string preview = raw.Type switch
            {
                "Focus" => "[Focus] " + GetFocusPreview(raw.Path),
                "Decision" => "[Decision] " + raw.Topic,
                "Work" => "[Work] " + raw.Topic,
                _ => raw.Topic,
            };

            result.Add(new TimelineEntryItem
            {
                IsGap = false,
                Date = raw.Date,
                EntryType = raw.Type,
                Preview = preview,
                FilePath = raw.Path,
                ProjectName = raw.ProjectName,
                ProjectHiddenKey = raw.ProjectHiddenKey,
            });

            prevDate = raw.Date;
        }

        // 統計
        int totalEntries = rawEntries.Count;
        int focusCount = rawEntries.Count(e => e.Type == "Focus");
        int decisionCount = rawEntries.Count(e => e.Type == "Decision");
        int workCount = rawEntries.Count(e => e.Type == "Work");
        var uniqueDays = rawEntries.Select(e => e.Date.Date).Distinct().Count();
        var newest = rawEntries.First().Date.ToString("yyyy-MM-dd");
        var oldest = rawEntries.Last().Date.ToString("yyyy-MM-dd");

        double activeRate = 0;
        if (daysBack > 0)
            activeRate = uniqueDays * 100.0 / daysBack;

        string typeSummary = $"Focus: {focusCount}  Decision: {decisionCount}  Work: {workCount}";

        string stats = daysBack > 0
            ? $"Total: {totalEntries} entries  ({typeSummary}) | Active: {uniqueDays} days | Period: {oldest} ~ {newest} | Rate: {activeRate:F1}%"
            : $"Total: {totalEntries} entries  ({typeSummary}) | Active: {uniqueDays} days | Period: {oldest} ~ {newest}";

        return (result, stats);
    }

    private static TimelineHeatmapBuildResult BuildHeatmap(
        IReadOnlyList<ProjectInfo> allProjects,
        ProjectInfo? selectedProject,
        int daysBack,
        string graphScope)
    {
        var scopeProjects = graphScope == "Selected project" && selectedProject != null
            ? new List<ProjectInfo> { selectedProject }
            : [.. allProjects];

        if (scopeProjects.Count == 0)
        {
            return new TimelineHeatmapBuildResult
            {
                EmptyText = "No project selected.",
            };
        }

        var cutoff = daysBack > 0
            ? DateTime.Today.AddDays(-daysBack)
            : DateTime.MinValue;

        var rawEntries = new List<TimelineRawEntry>();
        foreach (var project in scopeProjects)
            rawEntries.AddRange(BuildRawEntries(project, cutoff));

        if (rawEntries.Count == 0)
        {
            return new TimelineHeatmapBuildResult
            {
                EmptyText = "No timeline events found for this range.",
            };
        }

        DateTime startDate;
        DateTime endDate;

        if (daysBack > 0)
        {
            endDate = DateTime.Today;
            startDate = DateTime.Today.AddDays(-(daysBack - 1));
        }
        else
        {
            startDate = rawEntries.Min(e => e.Date).Date;
            endDate = rawEntries.Max(e => e.Date).Date;
        }

        if (startDate > endDate)
            startDate = endDate;

        var bucketDates = new List<DateTime>();
        for (var d = startDate.Date; d <= endDate.Date; d = d.AddDays(1))
            bucketDates.Add(d);

        var buckets = bucketDates
            .Select((date, index) => new TimelineHeatmapBucketItem
            {
                Date = date,
                Label = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ShortLabel = BuildShortBucketLabel(date, index, bucketDates.Count),
            })
            .ToList();

        var counts = rawEntries
            .GroupBy(e => (e.ProjectHiddenKey, Date: e.Date.Date))
            .ToDictionary(g => g.Key, g => g.Count());

        int maxCount = counts.Count > 0 ? counts.Values.Max() : 0;

        var projectsWithEntries = rawEntries
            .Select(e => e.ProjectHiddenKey)
            .Distinct()
            .ToHashSet(StringComparer.Ordinal);

        var rows = scopeProjects
            .Where(p => projectsWithEntries.Contains(p.HiddenKey))
            .Select(project =>
            {
                var row = new TimelineHeatmapRowItem
                {
                    ProjectName = project.DisplayName,
                    ProjectKey = project.HiddenKey,
                };

                foreach (var bucket in buckets)
                {
                    counts.TryGetValue((project.HiddenKey, bucket.Date.Date), out int count);

                    var intensity = maxCount > 0 ? (double)count / maxCount : 0d;
                    row.Cells.Add(new TimelineHeatmapCellItem
                    {
                        ProjectHiddenKey = project.HiddenKey,
                        BucketLabel = bucket.Label,
                        BucketDate = bucket.Date,
                        Count = count,
                        Intensity = intensity,
                        ToolTipText = count > 0
                            ? $"{project.DisplayName}\n{bucket.Label}\nEvents: {count}\nClick to open file"
                            : $"{project.DisplayName}\n{bucket.Label}\nEvents: 0",
                    });
                }

                return row;
            })
            .ToList();

        var stats = $"Projects: {rows.Count} | Buckets: {buckets.Count} days | Events: {rawEntries.Count} | Peak/day: {maxCount}";

        return new TimelineHeatmapBuildResult
        {
            Buckets = buckets,
            Rows = rows,
            Stats = stats,
            EmptyText = "No timeline events found for this range.",
        };
    }

    private static string BuildShortBucketLabel(DateTime date, int index, int totalCount)
    {
        if (index is 0 || index == totalCount - 1)
            return date.ToString("M/d", CultureInfo.InvariantCulture);

        return date.Day == 1
            ? date.ToString("M/d", CultureInfo.InvariantCulture)
            : "";
    }

    private static List<TimelineRawEntry> BuildRawEntries(ProjectInfo project, DateTime cutoff)
    {
        var histDir = Path.Combine(project.AiContextContentPath, "focus_history");
        var logDir = Path.Combine(project.AiContextContentPath, "decision_log");

        var rawEntries = new List<TimelineRawEntry>();

        // Focus History files
        if (Directory.Exists(histDir))
        {
            foreach (var file in Directory.EnumerateFiles(histDir, "*.md"))
            {
                var baseName = Path.GetFileNameWithoutExtension(file);
                if (DateTime.TryParseExact(baseName, "yyyy-MM-dd",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var date)
                    && date >= cutoff)
                {
                    rawEntries.Add(new TimelineRawEntry
                    {
                        Date = date,
                        Path = file,
                        Type = "Focus",
                        Topic = "",
                        ProjectName = project.Name,
                        ProjectHiddenKey = project.HiddenKey,
                    });
                }
            }
        }

        // Decision Log files
        if (Directory.Exists(logDir))
        {
            foreach (var file in Directory.EnumerateFiles(logDir, "*.md"))
            {
                var baseName = Path.GetFileNameWithoutExtension(file);
                if (baseName == "TEMPLATE")
                    continue;

                var match = Regex.Match(baseName, @"^(\d{4}-\d{2}-\d{2})_(.+)$");
                if (!match.Success)
                    continue;

                if (DateTime.TryParseExact(match.Groups[1].Value, "yyyy-MM-dd",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var date)
                    && date >= cutoff)
                {
                    rawEntries.Add(new TimelineRawEntry
                    {
                        Date = date,
                        Path = file,
                        Type = "Decision",
                        Topic = match.Groups[2].Value,
                        ProjectName = project.Name,
                        ProjectHiddenKey = project.HiddenKey,
                    });
                }
            }
        }

        // Work folders under shared/_work/
        // General:    _work/{year(4d)}/{yearMonth(6d)}/{yyyyMMdd}_{name}
        // Workstream: _work/{workstreamId}/{yearMonth(6d)}/{yyyyMMdd}_{name}
        var workRoot = Path.Combine(project.Path, "shared", "_work");
        if (Directory.Exists(workRoot))
            ScanWorkFolders(workRoot, project, cutoff, rawEntries);

        return rawEntries;
    }

    /// <summary>
    /// shared/_work/ 以下の日付フォルダをスキャンして rawEntries に追加する。
    /// General:    _work/{year(4d)}/{yearMonth(6d)}/{yyyyMMdd}_{name}
    /// Workstream: _work/{workstreamId}/{yearMonth(6d)}/{yyyyMMdd}_{name}
    /// </summary>
    private static void ScanWorkFolders(string workRoot, ProjectInfo project, DateTime cutoff, List<TimelineRawEntry> rawEntries)
    {
        string[] l1Dirs;
        try { l1Dirs = Directory.GetDirectories(workRoot); }
        catch (Exception ex) { Debug.WriteLine($"[Timeline] _work L1 error: {ex.Message}"); return; }

        foreach (var l1 in l1Dirs)
        {
            var l1Name = Path.GetFileName(l1)!;

            // year dir (4 digits) → get yearMonth dirs inside it
            // workstream dir (other) → treat its subdirs as yearMonth dirs
            string[] l2Dirs;
            try { l2Dirs = Directory.GetDirectories(l1); }
            catch { continue; }

            foreach (var l2 in l2Dirs)
            {
                if (!Regex.IsMatch(Path.GetFileName(l2)!, @"^\d{6}$"))
                    continue;

                string[] l3Dirs;
                try { l3Dirs = Directory.GetDirectories(l2); }
                catch { continue; }

                foreach (var l3 in l3Dirs)
                {
                    var name = Path.GetFileName(l3)!;
                    if (!Regex.IsMatch(name, @"^\d{8}_"))
                        continue;

                    if (!DateTime.TryParseExact(name[..8], "yyyyMMdd",
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out var date)
                        || date < cutoff)
                        continue;

                    rawEntries.Add(new TimelineRawEntry
                    {
                        Date = date,
                        Path = l3,
                        Type = "Work",
                        Topic = name[9..],
                        ProjectName = project.Name,
                        ProjectHiddenKey = project.HiddenKey,
                    });
                }
            }
        }
    }

    /// <summary>focus_history ファイルから「今やってること」セクションの最初の箇条書きを取得。</summary>
    private static string GetFocusPreview(string filePath)
    {
        try
        {
            var (content, _) = EncodingDetector.ReadFile(filePath);
            var lines = content.Split('\n');
            bool inSection = false;
            foreach (var raw in lines)
            {
                var line = raw.TrimEnd('\r');
                if (Regex.IsMatch(line, @"^## (今やってること|Now doing)"))
                {
                    inSection = true;
                    continue;
                }

                if (inSection && Regex.IsMatch(line, @"^## "))
                    break;

                if (inSection && Regex.IsMatch(line, @"^\s*-\s+\S"))
                {
                    var preview = Regex.Replace(line, @"^\s*-\s+", "").Trim();
                    return preview.Length > 80 ? preview[..77] + "..." : preview;
                }
            }

            // fallback: first non-empty line
            foreach (var raw in lines)
            {
                var trimmed = raw.TrimEnd('\r').Trim();
                if (trimmed.Length > 0
                    && !trimmed.StartsWith('#')
                    && !trimmed.StartsWith("<!--", StringComparison.Ordinal)
                    && !trimmed.StartsWith("---", StringComparison.Ordinal)
                    && trimmed != "-")
                {
                    return trimmed.Length > 80 ? trimmed[..77] + "..." : trimmed;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Timeline] Failed to read focus preview: {filePath}: {ex}");
        }

        return "";
    }

    private sealed class TimelineRawEntry
    {
        public DateTime Date { get; init; }
        public string Path { get; init; } = "";
        public string Type { get; init; } = "";
        public string Topic { get; init; } = "";
        public string ProjectName { get; init; } = "";
        public string ProjectHiddenKey { get; init; } = "";
    }

    private sealed class TimelineHeatmapBuildResult
    {
        public List<TimelineHeatmapBucketItem> Buckets { get; init; } = [];
        public List<TimelineHeatmapRowItem> Rows { get; init; } = [];
        public string Stats { get; init; } = "";
        public string EmptyText { get; init; } = "No timeline events found for this range.";
    }
}
