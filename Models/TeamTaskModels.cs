namespace ProjectCurator.Models;

/// <summary>
/// Team View ダイアログで担当者ごとに表示するカード。
/// </summary>
public class TeamMemberCard
{
    public string MemberName { get; set; } = "";
    public List<TeamTaskItem> Tasks { get; set; } = [];
    public int TaskCount => Tasks.Count;
}

/// <summary>
/// Team View ダイアログで表示する 1 タスク分のモデル。
/// </summary>
public class TeamTaskItem
{
    public string Name { get; set; } = "";
    public string? DueOn { get; set; }
    public bool IsOverdue { get; set; }
    public string ProjectTag { get; set; } = "";
    public string AsanaGid { get; set; } = "";
    public string AsanaUrl => $"https://app.asana.com/0/0/{AsanaGid}";

    /// <summary>MM/DD 形式の日付文字列。未設定の場合は "-"。</summary>
    public string DueDisplay => string.IsNullOrWhiteSpace(DueOn) || DueOn.Length < 7
        ? "-"
        : DueOn[5..].Replace('-', '/');
}
