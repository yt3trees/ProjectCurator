using System.Text.Json.Serialization;

namespace Curia.Models;

public enum ScheduleBlockKind
{
    Timed,   // 時刻指定 (例: 13:00〜15:00)
    AllDay,  // 終日 / 複数日またぎ (例: 4/15〜4/17)
}

public class ScheduleBlock
{
    /// <summary>ブロック識別用 GUID (移動/削除操作用)。</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Timed / AllDay の区別。</summary>
    public ScheduleBlockKind Kind { get; set; } = ScheduleBlockKind.Timed;

    /// <summary>タスク識別キー。AsanaTaskGid があればそれ、なければ合成キー。</summary>
    public string TaskIdentity { get; set; } = "";

    /// <summary>表示用プロジェクト名 (ProjectShortName)。</summary>
    public string ProjectShortName { get; set; } = "";

    /// <summary>表示用タスクタイトル (カード見出し)。キャッシュ用途。</summary>
    public string TitleSnapshot { get; set; } = "";

    // --- Timed 用 ---
    /// <summary>開始時刻 (ローカル時刻, 30分境界にスナップ)。Kind=Timed 時のみ有効。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? StartAt { get; set; }

    /// <summary>長さ (30分単位, 最小 1 = 30分, 最大 48 = 24時間)。Kind=Timed 時のみ有効。</summary>
    public int DurationSlots { get; set; } = 2;

    // --- AllDay 用 ---
    /// <summary>終日ブロックの開始日 (0:00 基準, ローカル時刻)。Kind=AllDay 時のみ有効。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? StartDate { get; set; }

    /// <summary>終日ブロックの終了日 (両端含む, 例: 4/15〜4/17 = 3日間)。Kind=AllDay 時のみ有効。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? EndDate { get; set; }

    /// <summary>ユーザーが手動で付けたメモ (任意)。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Note { get; set; }

    /// <summary>カード表示色のキー (optional, project 単位の自動割り当て or 手動)。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ColorKey { get; set; }

    // --- 算出プロパティ ---
    [JsonIgnore]
    public DateTime EndAtExclusive => Kind switch
    {
        ScheduleBlockKind.Timed  => StartAt!.Value.AddMinutes(DurationSlots * 30),
        ScheduleBlockKind.AllDay => EndDate!.Value.Date.AddDays(1),
        _ => throw new InvalidOperationException()
    };

    [JsonIgnore]
    public int SpanDays => Kind == ScheduleBlockKind.AllDay
        ? (int)(EndDate!.Value.Date - StartDate!.Value.Date).TotalDays + 1
        : 1;

    public ScheduleBlock Clone() => new()
    {
        Id = Id,
        Kind = Kind,
        TaskIdentity = TaskIdentity,
        ProjectShortName = ProjectShortName,
        TitleSnapshot = TitleSnapshot,
        StartAt = StartAt,
        DurationSlots = DurationSlots,
        StartDate = StartDate,
        EndDate = EndDate,
        Note = Note,
        ColorKey = ColorKey,
    };
}
