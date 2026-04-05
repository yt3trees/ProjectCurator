using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Curia.Models;

namespace Curia.Services;

/// <summary>
/// team-tasks.md をパースして TeamMemberCard のリストを返すパーサー。
/// </summary>
public class TeamTaskParser
{
    private static readonly Regex MemberHeadingRx = new(@"^## (.+)$", RegexOptions.Compiled);
    private static readonly Regex TaskLineRx = new(@"^- \[[ x]\] (.+)$", RegexOptions.Compiled);
    private static readonly Regex DueRx = new(@"\(Due: (\d{4}-\d{2}-\d{2})\)", RegexOptions.Compiled);
    private static readonly Regex ProjectTagRx = new(@"\[([^\]]+)\] \[\[Asana", RegexOptions.Compiled);
    private static readonly Regex GidRx = new(@"https://app\.asana\.com/0/0/(\d+)", RegexOptions.Compiled);
    private static readonly Regex LastSyncRx = new(@"^Last Sync: (.+)$", RegexOptions.Compiled);
    private static readonly Regex TrailingBracketRx = new(@"\[[^\]]*\]\s*$", RegexOptions.Compiled);

    public (List<TeamMemberCard> Members, string? LastSync) Parse(string filePath)
    {
        if (!File.Exists(filePath))
            return ([], null);

        string[] lines;
        try
        {
            lines = File.ReadAllLines(filePath, new UTF8Encoding(false));
        }
        catch
        {
            return ([], null);
        }

        var members = new List<TeamMemberCard>();
        TeamMemberCard? current = null;
        string? lastSync = null;
        var today = DateOnly.FromDateTime(DateTime.Today);

        foreach (var line in lines)
        {
            var syncMatch = LastSyncRx.Match(line);
            if (syncMatch.Success)
            {
                lastSync = syncMatch.Groups[1].Value.Trim();
                continue;
            }

            var memberMatch = MemberHeadingRx.Match(line);
            if (memberMatch.Success)
            {
                current = new TeamMemberCard { MemberName = memberMatch.Groups[1].Value.Trim() };
                members.Add(current);
                continue;
            }

            if (current == null) continue;

            var taskMatch = TaskLineRx.Match(line);
            if (!taskMatch.Success) continue;

            var content = taskMatch.Groups[1].Value;
            var item = new TeamTaskItem();

            // Due date
            var dueMatch = DueRx.Match(content);
            if (dueMatch.Success)
            {
                item.DueOn = dueMatch.Groups[1].Value;
                if (DateOnly.TryParse(item.DueOn, out var dueDate))
                    item.IsOverdue = dueDate < today;
            }

            // Project tag: text in [...] directly before [[Asana]...]
            var projectTagMatch = ProjectTagRx.Match(content);
            if (projectTagMatch.Success)
                item.ProjectTag = projectTagMatch.Groups[1].Value;

            // Asana GID
            var gidMatch = GidRx.Match(content);
            if (gidMatch.Success)
                item.AsanaGid = gidMatch.Groups[1].Value;

            // Task name: strip all suffixes
            var name = content;
            name = DueRx.Replace(name, "").Trim();
            var asanaIdx = name.LastIndexOf("[[Asana]", StringComparison.Ordinal);
            if (asanaIdx > 0) name = name[..asanaIdx].Trim();
            name = name.Replace("⚠", "").Trim();
            // Remove trailing [ProjectTag]
            name = TrailingBracketRx.Replace(name, "").Trim();
            item.Name = name;

            current.Tasks.Add(item);
        }

        return (members, lastSync);
    }
}
