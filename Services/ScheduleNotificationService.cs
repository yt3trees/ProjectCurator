using Curia.Models;

namespace Curia.Services;

/// <summary>
/// Timed スケジュールブロックの開始時刻にバルーン通知を表示するサービス。
/// 1分ごとにスケジュールをチェックし、開始時刻の1分以内に入ったブロックを通知する。
/// </summary>
public class ScheduleNotificationService : IDisposable
{
    private readonly ScheduleService _scheduleService;
    private readonly TrayService _trayService;

    private System.Threading.Timer? _timer;
    private readonly HashSet<string> _notifiedKeys = [];
    private string _notifiedDate = "";
    private bool _disposed;

    public ScheduleNotificationService(ScheduleService scheduleService, TrayService trayService)
    {
        _scheduleService = scheduleService;
        _trayService = trayService;
    }

    public void Start()
    {
        // 次の分の頭まで待ってから1分ごとに発火させる
        var now = DateTime.Now;
        var delayToNextMinute = TimeSpan.FromSeconds(60 - now.Second);
        _timer = new System.Threading.Timer(OnTick, null, delayToNextMinute, TimeSpan.FromMinutes(1));
    }

    private void OnTick(object? state)
    {
        try
        {
            CheckAndNotify();
        }
        catch
        {
            // 通知失敗はサイレントに無視する
        }
    }

    private void CheckAndNotify()
    {
        var now = DateTime.Now;
        var today = now.Date;
        var todayStr = today.ToString("yyyy-MM-dd");

        // 日付が変わったら通知済みセットをリセット
        if (_notifiedDate != todayStr)
        {
            _notifiedKeys.Clear();
            _notifiedDate = todayStr;
        }

        var weekStart = GetMondayOf(today);
        var blocks = _scheduleService.GetBlocksForWeek(weekStart);

        foreach (var block in blocks)
        {
            if (block.Kind != ScheduleBlockKind.Timed || !block.StartAt.HasValue) continue;

            var startAt = block.StartAt.Value;
            if (startAt.Date != today) continue;

            // 現在時刻が StartAt の ±1分 以内かチェック
            var diff = (startAt - now).TotalMinutes;
            if (diff is > -1.0 and <= 1.0)
            {
                var key = $"{block.Id}:{todayStr}";
                if (_notifiedKeys.Add(key))
                {
                    var timeText = startAt.ToString("HH:mm");
                    var title = string.IsNullOrWhiteSpace(block.TitleSnapshot)
                        ? "Schedule"
                        : block.TitleSnapshot;
                    _trayService.ShowBalloonTip(
                        title,
                        $"Starts at {timeText}",
                        timeoutMs: 5000);
                }
            }
        }
    }

    private static DateTime GetMondayOf(DateTime date)
    {
        int diff = (7 + (date.DayOfWeek - DayOfWeek.Monday)) % 7;
        return date.Date.AddDays(-diff);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer?.Dispose();
        _timer = null;
    }
}
