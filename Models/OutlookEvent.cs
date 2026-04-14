namespace Curia.Models;

/// <summary>
/// Outlook の AppointmentItem を Curia 用に変換した読み取り専用 DTO。
/// </summary>
public class OutlookEvent
{
    /// <summary>Outlook の EntryID (重複排除用)。</summary>
    public string EntryId { get; set; } = "";

    /// <summary>予定のタイトル (Subject)。</summary>
    public string Subject { get; set; } = "";

    /// <summary>開始日時 (ローカル時刻)。</summary>
    public DateTime Start { get; set; }

    /// <summary>終了日時 (ローカル時刻)。</summary>
    public DateTime End { get; set; }

    /// <summary>終日予定かどうか。</summary>
    public bool IsAllDay { get; set; }

    /// <summary>場所 (任意)。</summary>
    public string? Location { get; set; }

    /// <summary>カレンダーアカウント名 (表示用)。</summary>
    public string? CalendarName { get; set; }
}
