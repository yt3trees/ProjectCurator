using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Curia.Models;

namespace Curia.Services;

/// <summary>
/// スケジュールブロックの永続化サービス。
/// [baseDir]/_schedule/yyyy-MM.json に月単位で分割保存する。
/// </summary>
public class ScheduleService
{
    private readonly ConfigService _configService;
    private readonly object _lock = new();

    // 月キー "yyyy-MM" → ブロックリスト のキャッシュ
    private readonly Dictionary<string, List<ScheduleBlock>> _monthCache = [];
    private readonly HashSet<string> _loadedMonths = [];

    private CancellationTokenSource? _debounceCts;
    // debounce 中に保存が必要な月のセット
    private readonly HashSet<string> _dirtyMonths = [];

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public ScheduleService(ConfigService configService)
    {
        _configService = configService;
    }

    // ─── パス解決 ───────────────────────────────────────────────────

    /// <summary>
    /// _schedule/ サブフォルダの絶対パス。
    /// CloudSyncRoot/_config/_schedule/ を優先し、未設定時は ConfigDir/_schedule/。
    /// </summary>
    private string ScheduleDir
    {
        get
        {
            var settings = _configService.LoadSettings();
            var baseDir = !string.IsNullOrWhiteSpace(settings.CloudSyncRoot)
                ? Path.Combine(settings.CloudSyncRoot, "_config")
                : _configService.ConfigDir;
            return Path.Combine(baseDir, "_schedule");
        }
    }

    private string MonthFilePath(string monthKey)
        => Path.Combine(ScheduleDir, $"{monthKey}.json");

    // ─── 公開 API ───────────────────────────────────────────────────

    /// <summary>
    /// 週 (weekStart の月曜 0:00 〜 +7日) と重なる全ブロックを返す。
    /// 週が月をまたぐ場合は最大2ファイルをロードする。
    /// </summary>
    public IReadOnlyList<ScheduleBlock> GetBlocksForWeek(DateTime weekStart)
    {
        var weekEnd = weekStart.AddDays(7);
        var months = MonthKeysForRange(weekStart, weekEnd.AddDays(-1));
        foreach (var m in months) EnsureMonthLoaded(m);

        lock (_lock)
        {
            return months
                .Where(_monthCache.ContainsKey)
                .SelectMany(m => _monthCache[m])
                .Where(b => BlockOverlapsWeek(b, weekStart, weekEnd))
                .ToList()
                .AsReadOnly();
        }
    }

    public void AddBlock(ScheduleBlock block)
    {
        NormalizeBlock(block);
        var month = MonthKeyOf(block);
        EnsureMonthLoaded(month);
        lock (_lock)
        {
            if (!_monthCache.ContainsKey(month)) _monthCache[month] = [];
            _monthCache[month].Add(block);
        }
        ScheduleSave(month);
    }

    public void UpdateBlock(ScheduleBlock block)
    {
        NormalizeBlock(block);
        var newMonth = MonthKeyOf(block);
        EnsureMonthLoaded(newMonth);

        lock (_lock)
        {
            // 旧月から削除 (月またぎ移動に対応)
            foreach (var kv in _monthCache)
            {
                var removed = kv.Value.RemoveAll(b => b.Id == block.Id);
                if (removed > 0 && kv.Key != newMonth)
                    ScheduleSave(kv.Key); // 旧月も保存
            }
            // 新月に追加
            if (!_monthCache.ContainsKey(newMonth)) _monthCache[newMonth] = [];
            _monthCache[newMonth].Add(block);
        }
        ScheduleSave(newMonth);
    }

    public void RemoveBlock(string blockId)
    {
        lock (_lock)
        {
            foreach (var kv in _monthCache)
            {
                if (kv.Value.RemoveAll(b => b.Id == blockId) > 0)
                    ScheduleSave(kv.Key);
            }
        }
    }

    /// <summary>タスク削除時の整合: 同じ TaskIdentity を持つ全ブロックを削除する。</summary>
    public void RemoveBlocksByTaskIdentity(string taskIdentity)
    {
        lock (_lock)
        {
            foreach (var kv in _monthCache)
            {
                if (kv.Value.RemoveAll(b => b.TaskIdentity == taskIdentity) > 0)
                    ScheduleSave(kv.Key);
            }
        }
    }

    // ─── 月ファイルのロード ──────────────────────────────────────────

    private void EnsureMonthLoaded(string monthKey)
    {
        lock (_lock)
        {
            if (_loadedMonths.Contains(monthKey)) return;
            _loadedMonths.Add(monthKey);
        }
        TryLoadMonth(monthKey);
    }

    private void TryLoadMonth(string monthKey)
    {
        var path = MonthFilePath(monthKey);
        try
        {
            if (!File.Exists(path)) return;
            var json = File.ReadAllText(path, Encoding.UTF8);
            var data = JsonSerializer.Deserialize<ScheduleData>(json, JsonOpts);
            if (data?.Blocks == null) return;
            lock (_lock) { _monthCache[monthKey] = data.Blocks; }
        }
        catch { /* ファイル破損時は空スタート */ }
    }

    // ─── 保存 (debounce) ────────────────────────────────────────────

    /// <summary>
    /// 200ms の debounce で SaveToDisk を呼ぶ。
    /// ドラッグ中の連続 UpdateBlock に対応し、複数月が dirty なら一括保存する。
    /// </summary>
    private void ScheduleSave(string monthKey)
    {
        lock (_lock) { _dirtyMonths.Add(monthKey); }

        _debounceCts?.Cancel();
        _debounceCts = new CancellationTokenSource();
        var token = _debounceCts.Token;
        Task.Delay(200, token).ContinueWith(_ =>
        {
            if (!token.IsCancellationRequested)
                FlushDirtyMonths();
        }, TaskContinuationOptions.OnlyOnRanToCompletion);
    }

    private void FlushDirtyMonths()
    {
        List<string> toSave;
        lock (_lock)
        {
            toSave = [.. _dirtyMonths];
            _dirtyMonths.Clear();
        }
        foreach (var m in toSave)
            SaveMonth(m);
    }

    private void SaveMonth(string monthKey)
    {
        List<ScheduleBlock> snapshot;
        lock (_lock)
        {
            if (!_monthCache.TryGetValue(monthKey, out var list)) return;
            snapshot = [.. list];
        }
        try
        {
            var dir = ScheduleDir;
            Directory.CreateDirectory(dir);
            var path = MonthFilePath(monthKey);
            var data = new ScheduleData { Version = 1, Blocks = snapshot };
            var json = JsonSerializer.Serialize(data, JsonOpts);
            File.WriteAllText(path, json, Encoding.UTF8);
        }
        catch { /* 書き込み失敗は無視 */ }
    }

    // ─── ユーティリティ ──────────────────────────────────────────────

    private static void NormalizeBlock(ScheduleBlock block)
    {
        if (block.Kind == ScheduleBlockKind.Timed && block.StartAt.HasValue)
        {
            var dt = block.StartAt.Value;
            block.StartAt = new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour,
                dt.Minute >= 30 ? 30 : 0, 0, dt.Kind);
        }
        if (block.Kind == ScheduleBlockKind.AllDay &&
            block.EndDate.HasValue && block.StartDate.HasValue &&
            block.EndDate.Value < block.StartDate.Value)
            block.EndDate = block.StartDate;
    }

    /// <summary>ブロックの開始日から月キー "yyyy-MM" を算出する。</summary>
    private static string MonthKeyOf(ScheduleBlock block)
    {
        var date = block.Kind == ScheduleBlockKind.Timed
            ? (block.StartAt ?? DateTime.Today)
            : (block.StartDate ?? DateTime.Today);
        return date.ToString("yyyy-MM");
    }

    /// <summary>日付範囲に含まれる月キーの一覧を返す。</summary>
    private static IEnumerable<string> MonthKeysForRange(DateTime from, DateTime to)
    {
        var cur = new DateTime(from.Year, from.Month, 1);
        var end = new DateTime(to.Year, to.Month, 1);
        while (cur <= end)
        {
            yield return cur.ToString("yyyy-MM");
            cur = cur.AddMonths(1);
        }
    }

    private static bool BlockOverlapsWeek(ScheduleBlock b, DateTime weekStart, DateTime weekEnd)
    {
        if (b.Kind == ScheduleBlockKind.Timed && b.StartAt.HasValue)
        {
            var end = b.StartAt.Value.AddMinutes(b.DurationSlots * 30);
            return b.StartAt.Value < weekEnd && end > weekStart;
        }
        if (b.Kind == ScheduleBlockKind.AllDay && b.StartDate.HasValue && b.EndDate.HasValue)
        {
            return b.StartDate.Value.Date < weekEnd && b.EndDate.Value.Date >= weekStart;
        }
        return false;
    }
}

file class ScheduleData
{
    public int Version { get; set; } = 1;
    public List<ScheduleBlock> Blocks { get; set; } = [];
}
