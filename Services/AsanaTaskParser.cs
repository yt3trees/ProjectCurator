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

    private static readonly Regex CollabTagRegex  = new(@"\[コラボ\]",               RegexOptions.Compiled);
    private static readonly Regex PriorityRegex   = new(@"\[(?<prio>最高|High|Medium|Low)\]", RegexOptions.Compiled);
    private static readonly Regex AsanaIdRegex    = new(@"\[id:(?<id>[^\]]+)\]",    RegexOptions.Compiled);
    private static readonly Regex DueDateRegex    = new(@"期日:\s*(?<date>\d{4}-\d{2}-\d{2})", RegexOptions.Compiled);

    public AsanaTaskParseResult Parse(string content, string sourcePath = "")
    {
        var result = new AsanaTaskParseResult { SourcePath = sourcePath };

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.TrimEnd();
            if (string.IsNullOrWhiteSpace(line)) continue;

            var match = TaskLineRegex.Match(line);
            if (!match.Success) continue;

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
                .Replace("[コラボ]", "")
                .Replace("[担当]", "")
                .Replace($"[{priority}]", "")
                .Replace($"[id:{id}]", "")
                .Replace($"期日: {dueDate}", "")
                .Replace($"期日:{dueDate}", "")
                .Trim();

            var task = new ParsedAsanaTask
            {
                Title        = string.IsNullOrWhiteSpace(cleanTitle) ? titleRaw : cleanTitle,
                Id           = string.IsNullOrWhiteSpace(id)      ? null : id,
                Status       = status,
                Priority     = priority,
                AssigneeType = assigneeType,
                DueDate      = string.IsNullOrWhiteSpace(dueDate) ? null : dueDate,
                RawLine      = rawLine
            };

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
            if (prio is "最高" or "High")
                return AsanaTaskStatus.InProgress;
        }

        return AsanaTaskStatus.NotStarted;
    }
}
