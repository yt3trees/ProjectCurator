using System.Collections.Generic;
using System.Linq;

namespace ProjectCurator.Models;

public class MeetingAnalysisResult
{
    public List<MeetingDecision> Decisions { get; set; } = [];
    public MeetingFocusUpdate FocusUpdate  { get; set; } = new();
    public MeetingTensions Tensions        { get; set; } = new();
    public MeetingAsanaTasks AsanaTasks   { get; set; } = new();
    public string DebugSystemPrompt { get; set; } = "";
    public string DebugUserPrompt   { get; set; } = "";
    public string DebugResponse     { get; set; } = "";
}

public class MeetingDecision
{
    public string FilenameTopic  { get; set; } = "";
    public string Title          { get; set; } = "";
    public string Status         { get; set; } = "";
    public string Trigger        { get; set; } = "";
    public string Context        { get; set; } = "";
    public string OptionAName    { get; set; } = "";
    public string OptionAPros    { get; set; } = "";
    public string OptionACons    { get; set; } = "";
    public string OptionBName    { get; set; } = "";
    public string OptionBPros    { get; set; } = "";
    public string OptionBCons    { get; set; } = "";
    public string Chosen         { get; set; } = "";
    public string Why            { get; set; } = "";
    public string Risk           { get; set; } = "";
    public string RevisitTrigger { get; set; } = "";
    public bool   IsSelected     { get; set; } = true;
}

public class MeetingFocusUpdate
{
    public List<string> RecentContext { get; set; } = [];
    public List<string> NextActions   { get; set; } = [];
    public bool   IsSelected      { get; set; } = true;
    public string ProposedContent { get; set; } = "";
    public string CurrentContent  { get; set; } = "";
}

public class MeetingTensions
{
    public List<string> TechnicalQuestions { get; set; } = [];
    public List<string> Tradeoffs          { get; set; } = [];
    public List<string> Concerns           { get; set; } = [];
    public bool   IsSelected     { get; set; } = true;
    public string AppendContent  { get; set; } = "";
    public string CurrentContent { get; set; } = "";
    public bool HasItems => TechnicalQuestions.Any() || Tradeoffs.Any() || Concerns.Any();
}

public class MeetingAsanaTask
{
    public string Title       { get; set; } = "";
    public string Priority    { get; set; } = "";  // "High" | "Medium" | "Low" | ""
    public string Notes       { get; set; } = "";
    public bool   IsSelected  { get; set; } = true;
    // ダイアログで設定されるタスクごとの Asana 設定
    public string ProjectGid  { get; set; } = "";
    public string ProjectName { get; set; } = "";
    public string SectionGid  { get; set; } = "";
    public string SectionName { get; set; } = "";
    public string DueOn       { get; set; } = "";  // "YYYY-MM-DD"
    public string DueTime     { get; set; } = "";  // "HH:mm" (apply 時に DueAt へ変換)
}

public class MeetingAsanaTasks
{
    public List<MeetingAsanaTask> Tasks         { get; set; } = [];
    public bool   IsSelected                    { get; set; } = true;
    public string AppendContent                 { get; set; } = "";
    public bool   HasItems                      => Tasks.Any();
    // ダイアログで選択されたプロジェクト/セクション (Apply 時に使用)
    public string SelectedProjectGid  { get; set; } = "";
    public string SelectedProjectName { get; set; } = "";
    public string SelectedSectionGid  { get; set; } = "";
    public string SelectedSectionName { get; set; } = "";
}

public class MeetingAsanaApplyResult
{
    public string? FilePath      { get; set; }
    public int ApiSuccessCount   { get; set; }
    public int ApiFailCount      { get; set; }
    public List<string> Errors   { get; set; } = [];
    public bool HasApiResult     => ApiSuccessCount > 0 || ApiFailCount > 0;
}

public class MeetingNotesInputResult
{
    public string  MeetingNotes  { get; set; } = "";
    public string? WorkstreamId  { get; set; }
}
