using System.IO;
using System.Text.RegularExpressions;
using ProjectCurator.Models;

namespace ProjectCurator.Services;

/// <summary>
/// asana-tasks.md をルールベースで解析し、進行中/完了/未着手/コラボタスクに分類する。
/// </summary>
public class AsanaTaskParser
{
    // "- [ ] タイトル" / "- [x] タイトル" / "- 🔄 タイトル" / "- ✅ タイトル"
    private static readonly Regex TaskLineRegex = new(
        @"^\s*- (?<check>\[[ x]\]|🔄|✅)\s+(?<title>.+)$",
        RegexOptions.Compiled);

    private static readonly Regex CollabTagRegex  = new(@"\[Collab\]",               RegexOptions.Compiled);
    private static readonly Regex PriorityRegex   = new(@"\[(?<prio>High|Medium|Low)\]", RegexOptions.Compiled);
    private static readonly Regex AsanaIdRegex    = new(@"\[id:(?<id>[^\]]+)\]",    RegexOptions.Compiled);
    private static readonly Regex DueDateRegex    = new(@"\(Due:\s*(?<date>\d{4}-\d{2}-\d{2})\)", RegexOptions.Compiled);

    private const int MaxDescriptionLines = 3;
    // "    > テキスト" または "    >" (空行) の blockquote 行
    private static readonly Regex DescLineRegex = new(@"^    > ?", RegexOptions.Compiled);

    public AsanaTaskParseResult Parse(string content, string sourcePath = "")
    {
        var result = new AsanaTaskParseResult { SourcePath = sourcePath };

        // インデントなしの直前の親タスクを追跡する
        string? currentParentTitle = null;
        ParsedAsanaTask? lastTask = null;
        var descLines = new List<string>();

        void FlushDescription()
        {
            if (lastTask != null && descLines.Count > 0)
                lastTask.Description = string.Join(" / ", descLines);
            descLines.Clear();
        }

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.TrimEnd();

            // blockquote 行: 直前タスクの概要として収集 (最大 MaxDescriptionLines 行)
            if (DescLineRegex.IsMatch(line) && lastTask != null && descLines.Count < MaxDescriptionLines)
            {
                var text = DescLineRegex.Replace(line, "").Trim();
                if (!string.IsNullOrEmpty(text))
                    descLines.Add(text);
                continue;
            }

            if (string.IsNullOrWhiteSpace(line)) continue;

            var match = TaskLineRegex.Match(line);
            if (!match.Success) continue;

            // 新しいタスク行が来たら前のタスクの概要を確定
            FlushDescription();

            // 先頭スペース数でサブタスクかどうか判定
            var leadingSpaces = line.Length - line.TrimStart().Length;
            var isSubtask = leadingSpaces > 0;

            var checkStr  = match.Groups["check"].Value;
            var titleRaw  = match.Groups["title"].Value.Trim();

            var status       = DetermineStatus(checkStr, titleRaw);
            var assigneeType = CollabTagRegex.IsMatch(titleRaw)
                ? AsanaTaskAssigneeType.Collaborator
                : AsanaTaskAssigneeType.Owner;

            var priority = PriorityRegex.Match(titleRaw).Groups["prio"].Value;
            var id       = AsanaIdRegex.Match(titleRaw).Groups["id"].Value;
            var dueDate  = DueDateRegex.Match(titleRaw).Groups["date"].Value;

            // タイトルのタグを除いた表示名
            var cleanTitle = titleRaw
                .Replace("[Collab]", "")
                .Replace("[Owner]", "")
                .Replace($"[{priority}]", "")
                .Replace($"[id:{id}]", "")
                .Replace($"(Due: {dueDate})", "")
                .Replace($"(Due:{dueDate})", "")
                .Trim();

            var resolvedTitle = string.IsNullOrWhiteSpace(cleanTitle) ? titleRaw : cleanTitle;

            // トップレベルタスクなら親タスクとして記録
            if (!isSubtask)
                currentParentTitle = resolvedTitle;

            var task = new ParsedAsanaTask
            {
                Title        = resolvedTitle,
                Id           = string.IsNullOrWhiteSpace(id)      ? null : id,
                Status       = status,
                Priority     = priority,
                AssigneeType = assigneeType,
                DueDate      = string.IsNullOrWhiteSpace(dueDate) ? null : dueDate,
                RawLine      = rawLine,
                ParentTitle  = isSubtask ? currentParentTitle : null,
            };

            lastTask = task;

            if (assigneeType == AsanaTaskAssigneeType.Collaborator)
            {
                result.Collaborating.Add(task);
            }
            else
            {
                switch (status)
                {
                    case AsanaTaskStatus.InProgress:  result.InProgress.Add(task);  break;
                    case AsanaTaskStatus.Completed:   result.Completed.Add(task);   break;
                    default:                          result.NotStarted.Add(task);  break;
                }
            }
        }

        FlushDescription(); // 末尾タスクの概要を確定
        return result;
    }

    public AsanaTaskParseResult ParseFile(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"asana-tasks.md not found: {filePath}");

        var content = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
        return Parse(content, filePath);
    }

    // -----------------------------------------------------------------------
    private static AsanaTaskStatus DetermineStatus(string checkStr, string title)
    {
        // 完了
        if (checkStr is "✅" or "[x]")
            return AsanaTaskStatus.Completed;

        // 進行中
        if (checkStr == "🔄")
            return AsanaTaskStatus.InProgress;

        // "[ ]" の場合は優先度で判定
        if (checkStr == "[ ]")
        {
            var prio = PriorityRegex.Match(title).Groups["prio"].Value;
            if (prio is "High")
                return AsanaTaskStatus.InProgress;
        }

        return AsanaTaskStatus.NotStarted;
    }
}
