using System.Net.Http;
using Curia.Models;
using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;

namespace Curia.Services;

/// <summary>
/// ICS (iCalendar) URL からカレンダーイベントを取得するサービス。
/// 新しい Outlook / Google Calendar など COM 非対応環境向けの代替手段。
/// </summary>
public class IcsCalendarService
{
    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
    };

    /// <summary>
    /// 指定 URL の ICS を取得し、weekStart の週に重なるイベントを返す。
    /// 失敗時は空リストを返す (例外をスローしない)。
    /// </summary>
    public async Task<IReadOnlyList<OutlookEvent>> GetEventsForWeekAsync(
        string icsUrl, DateTime weekStart)
    {
        try
        {
            var icsContent = await _http.GetStringAsync(icsUrl);
            return ParseWeekEvents(icsContent, weekStart);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[IcsCalendarService] {ex.Message}");
            throw; // SettingsViewModel の TestIcs でエラー表示するため伝搬
        }
    }

    private static IReadOnlyList<OutlookEvent> ParseWeekEvents(
        string icsContent, DateTime weekStart)
    {
        var weekEnd  = weekStart.AddDays(7);
        var calendar = Calendar.Load(icsContent);
        var events   = new List<OutlookEvent>();

        foreach (var vEvent in calendar.Events)
        {
            if (vEvent.DtStart == null) continue;

            if (vEvent.RecurrenceRules == null || vEvent.RecurrenceRules.Count == 0)
            {
                // 繰り返しなし: ローカル時刻に変換して週範囲チェック
                AddIfOverlaps(vEvent, ToLocal(vEvent.DtStart),
                    vEvent.DtEnd != null ? ToLocal(vEvent.DtEnd) : null,
                    weekStart, weekEnd, events);
            }
            else
            {
                // 繰り返しあり: DTSTART のローカル日から開始して各週の出現を展開
                ExpandRecurring(vEvent, weekStart, weekEnd, events);
            }
        }

        return events.AsReadOnly();
    }

    private static void AddIfOverlaps(
        CalendarEvent vEvent,
        DateTime start, DateTime? endRaw,
        DateTime weekStart, DateTime weekEnd,
        List<OutlookEvent> result)
    {
        bool isAllDay = !vEvent.DtStart.HasTime;
        // 終日イベントは End が翌日 00:00 なのでそのまま使う
        var end = endRaw ?? (isAllDay ? start.AddDays(1) : start.AddHours(1));

        // 週と重なるか
        if (start >= weekEnd || end <= weekStart) return;

        result.Add(new OutlookEvent
        {
            EntryId      = $"{vEvent.Uid}_{start:yyyyMMddHHmm}",
            Subject      = vEvent.Summary ?? "(No title)",
            Start        = start,
            End          = end,
            IsAllDay     = isAllDay,
            Location     = string.IsNullOrWhiteSpace(vEvent.Location) ? null : vEvent.Location,
            CalendarName = "ICS",
        });
    }

    private static void ExpandRecurring(
        CalendarEvent vEvent,
        DateTime weekStart, DateTime weekEnd,
        List<OutlookEvent> result)
    {
        // CalDateTime を TZID なし浮動時刻で生成して GetOccurrences を呼ぶ
        // TZID 付きイベントとの比較は ToLocal 後にフィルタする
        var calRangeStart = new CalDateTime(weekStart.Year, weekStart.Month, weekStart.Day, 0, 0, 0);

        IEnumerable<Occurrence> occs;
        try
        {
            occs = vEvent.GetOccurrences(calRangeStart);
        }
        catch
        {
            return;
        }

        bool isAllDay = !vEvent.DtStart.HasTime;

        foreach (var occ in occs)
        {
            if (occ.Period?.StartTime == null) continue;

            var start = ToLocal(occ.Period.StartTime);
            if (start >= weekEnd) break; // 以降は不要 (昇順前提)
            if (start < weekStart) continue;

            var end = occ.Period.EndTime != null
                ? ToLocal(occ.Period.EndTime)
                : (isAllDay ? start.AddDays(1) : start.AddHours(1));

            result.Add(new OutlookEvent
            {
                EntryId      = $"{vEvent.Uid}_{start:yyyyMMddHHmm}",
                Subject      = vEvent.Summary ?? "(No title)",
                Start        = start,
                End          = end,
                IsAllDay     = isAllDay,
                Location     = string.IsNullOrWhiteSpace(vEvent.Location) ? null : vEvent.Location,
                CalendarName = "ICS",
            });
        }
    }

    private static DateTime ToLocal(CalDateTime icalDt)
    {
        var dt = icalDt.Value;
        if (icalDt.IsUtc)
            return DateTime.SpecifyKind(dt, DateTimeKind.Utc).ToLocalTime();

        // TZID 付きの場合は TimeZoneInfo 経由で変換
        if (!string.IsNullOrEmpty(icalDt.TzId))
        {
            try
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById(icalDt.TzId)
                      ?? TimeZoneInfo.FindSystemTimeZoneById(IanaToWindows(icalDt.TzId));
                return TimeZoneInfo.ConvertTimeToUtc(
                    DateTime.SpecifyKind(dt, DateTimeKind.Unspecified), tz).ToLocalTime();
            }
            catch { /* 変換失敗時はローカルとして扱う */ }
        }

        return DateTime.SpecifyKind(dt, DateTimeKind.Local);
    }

    /// <summary>代表的な IANA タイムゾーン名を Windows タイムゾーン ID に変換する。</summary>
    private static string IanaToWindows(string ianaId) => ianaId switch
    {
        "Asia/Tokyo"       => "Tokyo Standard Time",
        "America/New_York" => "Eastern Standard Time",
        "America/Chicago"  => "Central Standard Time",
        "America/Denver"   => "Mountain Standard Time",
        "America/Los_Angeles" => "Pacific Standard Time",
        "Europe/London"    => "GMT Standard Time",
        "Europe/Paris"     => "W. Europe Standard Time",
        "UTC"              => "UTC",
        _                  => ianaId,
    };
}
