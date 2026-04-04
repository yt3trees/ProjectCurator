using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ProjectCurator.Helpers;
using ProjectCurator.Models;

namespace ProjectCurator.Services;

public class AsanaSyncService
{
    private readonly ConfigService _configService;
    private static readonly HttpClient Http = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly Regex MemoStartRx = new(@"<!-- Memo area for (\w+) -->", RegexOptions.Compiled);
    private static readonly Regex TaskLineRx = new(@"^\s*- \[[ x]\]", RegexOptions.Compiled);
    private static readonly Regex HeadingRx = new(@"^#", RegexOptions.Compiled);
    private static readonly Regex ProjectTagSuffixRx = new(@"\s*(?:\[Domain\]|\[Mini\])+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex WorkstreamIdRx = new(@"^[a-z0-9][a-z0-9-]*$", RegexOptions.Compiled);

    public AsanaSyncService(ConfigService configService)
    {
        _configService = configService;
    }

    public async Task RunAsync(Action<string>? log = null, bool skipHiddenProjects = true, CancellationToken ct = default)
    {
        void Log(string line) => log?.Invoke(line + Environment.NewLine);

        var asanaGlobal = _configService.LoadAsanaGlobalConfig();
        var paths = _configService.LoadSettings();

        var token = Environment.GetEnvironmentVariable("ASANA_TOKEN");
        if (string.IsNullOrWhiteSpace(token))
            token = asanaGlobal.AsanaToken;
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("ASANA_TOKEN または _config/asana_global.json の asana_token が必要です。");

        var userGid = Environment.GetEnvironmentVariable("ASANA_USER_GID");
        if (string.IsNullOrWhiteSpace(userGid))
            userGid = asanaGlobal.UserGid;
        if (string.IsNullOrWhiteSpace(userGid))
            throw new InvalidOperationException("_config/asana_global.json の user_gid (または ASANA_USER_GID) が必要です。");

        var syncRoot = paths.CloudSyncRoot.Trim();
        var obsidianRoot = paths.ObsidianVaultRoot.Trim();
        if (string.IsNullOrWhiteSpace(syncRoot) || string.IsNullOrWhiteSpace(obsidianRoot))
            throw new InvalidOperationException("paths 設定 (cloudSyncRoot / obsidianVaultRoot) が不足しています。");

        var personalProjectGids = asanaGlobal.PersonalProjectGids ?? [];

        var outputFile = Path.Combine(obsidianRoot, "asana-tasks-view.md");

        // 非表示プロジェクトのフィルタリング
        HashSet<string>? hiddenSet = null;
        if (skipHiddenProjects)
        {
            var hidden = _configService.LoadHiddenProjects();
            if (hidden.Count > 0)
            {
                hiddenSet = new HashSet<string>(hidden, StringComparer.OrdinalIgnoreCase);
                Log($"[Skip hidden] {hidden.Count} hidden projects will be excluded from sync.");
            }
        }

        Log("[1/5] Discovering projects with asana_config.json...");
        var discovered = DiscoverProjects(syncRoot, Log);

        // 非表示プロジェクトを除外
        if (hiddenSet != null && hiddenSet.Count > 0)
        {
            var before = discovered.Count;
            discovered = discovered.Where(p => !hiddenSet.Contains(p.Name)).ToList();
            var skipped = before - discovered.Count;
            if (skipped > 0)
                Log($"  Skipped {skipped} hidden project(s).");
        }

        if (discovered.Count == 0 && personalProjectGids.Count == 0)
        {
            Log("No projects with asana_config.json found and no personal projects configured.");
            return;
        }

        Log("");
        Log("[2/5] Fetching tasks for each project...");
        var allProjectData = new List<ProjectData>();

        foreach (var proj in discovered)
        {
            ct.ThrowIfCancellationRequested();
            Log("");
            Log($"  --- {proj.Name} ---");

            var sections = new List<ProjectSection>();
            foreach (var gid in proj.AsanaProjectGids)
            {
                var asanaProjectName = await FetchProjectNameAsync(gid, token, Log, ct);
                Log($"    Fetching: {asanaProjectName} ({gid})");

                var tasks = await FetchTasksForProjectAsync(gid, token, Log, ct);
                tasks = tasks.Where(t => IsOwnedOrCollaborating(t, userGid)).ToList();
                foreach (var task in tasks)
                    task.SourceProjectGid = gid;
                Log($"    -> {tasks.Count} tasks (担当/コラボのみ)");

                foreach (var task in tasks.Where(t => t.NumSubtasks > 0))
                    task.SubtasksData = await FetchSubtasksAsync(task.Gid, token, Log, ct);

                sections.Add(new ProjectSection(asanaProjectName, gid, tasks));
            }

            allProjectData.Add(new ProjectData(proj, sections, []));
        }

        Log("");
        Log("[3/5] Fetching personal project tasks...");
        var unmatchedPersonal = new List<AsanaTask>();

        foreach (var gid in personalProjectGids)
        {
            ct.ThrowIfCancellationRequested();
            var asanaProjectName = await FetchProjectNameAsync(gid, token, Log, ct);
            Log($"  Fetching: {asanaProjectName} ({gid})");

            var tasks = await FetchTasksForProjectAsync(gid, token, Log, ct);
            tasks = tasks.Where(t => IsOwnedOrCollaborating(t, userGid)).ToList();
            foreach (var task in tasks)
                task.SourceProjectGid = gid;
            Log($"  -> {tasks.Count} tasks (担当/コラボのみ)");

            foreach (var task in tasks)
            {
                if (task.NumSubtasks > 0)
                    task.SubtasksData = await FetchSubtasksAsync(task.Gid, token, Log, ct);

                var anken = GetCustomFieldValue(task, "Project");
                var matched = false;

                if (!string.IsNullOrWhiteSpace(anken))
                {
                    foreach (var projData in allProjectData)
                    {
                        var projName = projData.Project.Name;
                        var baseName = ProjectTagSuffixRx.Replace(projName, "");
                        if (anken == projName || anken == baseName || projData.Project.AnkenAliases.Contains(anken))
                        {
                            projData.PersonalTasks.Add(task);
                            matched = true;
                            break;
                        }
                    }
                }

                if (!matched)
                    unmatchedPersonal.Add(task);
            }
        }

        var distributed = allProjectData.Sum(p => p.PersonalTasks.Count);
        Log($"  Distributed {distributed} to projects, {unmatchedPersonal.Count} unmatched");

        Log("");
        Log("[4/5] Writing per-project files...");
        var summaryData = new List<SummaryProjectData>();

        foreach (var projData in allProjectData)
        {
            ct.ThrowIfCancellationRequested();
            var proj = projData.Project;
            var obsidianPath = proj.Name == "_INHOUSE"
                ? Path.Combine(obsidianRoot, "_INHOUSE")
                : Path.Combine(obsidianRoot, proj.RelativePath);

            var outputPath = Path.Combine(obsidianPath, "asana-tasks.md");
            var hasTasks = projData.Sections.Count > 0 || projData.PersonalTasks.Count > 0;
            var hasConfig = proj.HasAsanaConfigFile;
            var fileExists = File.Exists(outputPath);

            if (!(hasTasks || hasConfig || fileExists))
                continue;

            var split = SplitWorkstreamTasks(projData);
            WriteProjectFile(outputPath, proj.Name, split.RootSections, split.RootPersonalTasks, userGid);
            Log($"  Output: {outputPath}");

            foreach (var (workstreamId, wsTasks) in split.WorkstreamTasks.OrderBy(kv => kv.Key))
            {
                var wsOutput = Path.Combine(obsidianPath, "workstreams", workstreamId, "asana-tasks.md");
                WriteWorkstreamFile(wsOutput, proj.Name, workstreamId, wsTasks, userGid);
                Log($"  Output: {wsOutput}");
            }

            summaryData.Add(new SummaryProjectData(proj.Name, projData.Sections, projData.PersonalTasks));
        }

        if (unmatchedPersonal.Count > 0)
        {
            var personalOutput = Path.Combine(obsidianRoot, "asana-tasks-personal.md");
            WritePersonalFile(personalOutput, unmatchedPersonal, userGid);
            Log($"  Output: {personalOutput}");
        }

        Log("");
        Log("[5/5] Writing global summary...");
        WriteGlobalSummary(outputFile, summaryData, unmatchedPersonal, userGid);
        Log($"  Output: {outputFile}");
        Log("");
        Log($"Sync complete! ({allProjectData.Count} projects processed)");
    }

    private static List<DiscoveredProject> DiscoverProjects(string boxRoot, Action<string> log)
    {
        var projects = new List<DiscoveredProject>();
        var scanDirs = new (string dir, string prefix)[]
        {
            (boxRoot, "Projects"),
            (Path.Combine(boxRoot, "_mini"), "Projects/_mini"),
            (Path.Combine(boxRoot, "_domains"), "Projects/_domains"),
            (Path.Combine(boxRoot, "_domains", "_mini"), "Projects/_domains/_mini"),
        };

        foreach (var (scanDir, prefix) in scanDirs)
        {
            if (!Directory.Exists(scanDir))
                continue;

            foreach (var entry in Directory.EnumerateDirectories(scanDir))
            {
                var name = Path.GetFileName(entry);
                if (name.StartsWith('_') && name != "_INHOUSE")
                    continue;

                var configPath = Path.Combine(entry, "asana_config.json");
                var hasConfigFile = File.Exists(configPath);
                var cfg = hasConfigFile ? LoadAsanaProjectConfig(configPath) : new AsanaProjectConfig();

                var aliasesInfo = cfg.AnkenAliases.Count > 0
                    ? $", aliases: [{string.Join(", ", cfg.AnkenAliases)}]"
                    : "";
                var wsMapInfo = cfg.WorkstreamProjectMap.Count > 0
                    ? $", ws-map: {cfg.WorkstreamProjectMap.Count}"
                    : "";
                log($"  Found: {prefix}/{name} ({cfg.AsanaProjectGids.Count} Asana projects{aliasesInfo}{wsMapInfo})");

                projects.Add(new DiscoveredProject(
                    Name: name,
                    BoxPath: entry,
                    RelativePath: $"{prefix}/{name}",
                    AsanaProjectGids: cfg.AsanaProjectGids,
                    AnkenAliases: cfg.AnkenAliases,
                    WorkstreamProjectMap: cfg.WorkstreamProjectMap,
                    WorkstreamFieldName: string.IsNullOrWhiteSpace(cfg.WorkstreamFieldName) ? "workstream-id" : cfg.WorkstreamFieldName.Trim(),
                    HasAsanaConfigFile: hasConfigFile));
            }
        }

        return projects;
    }

    private static AsanaProjectConfig LoadAsanaProjectConfig(string path)
    {
        try
        {
            var (content, _) = EncodingDetector.ReadFile(path);
            return JsonSerializer.Deserialize<AsanaProjectConfig>(content, JsonOptions) ?? new AsanaProjectConfig();
        }
        catch
        {
            return new AsanaProjectConfig();
        }
    }

    private static async Task<string> FetchProjectNameAsync(
        string projectGid,
        string token,
        Action<string> log,
        CancellationToken ct)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var uri = $"https://app.asana.com/api/1.0/projects/{projectGid}?opt_fields=name";
                var req = new HttpRequestMessage(HttpMethod.Get, uri);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                using var resp = await Http.SendAsync(req, ct);
                if ((int)resp.StatusCode == 429)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2 * (attempt + 1)), ct);
                    continue;
                }

                if (!resp.IsSuccessStatusCode)
                {
                    log($"  WARNING: Failed to fetch project name for {projectGid}: {(int)resp.StatusCode}");
                    return projectGid;
                }

                var raw = await resp.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.TryGetProperty("data", out var data) &&
                    data.TryGetProperty("name", out var name))
                {
                    return name.GetString() ?? projectGid;
                }
                return projectGid;
            }
            catch (Exception ex)
            {
                log($"  WARNING: Failed to fetch project name for {projectGid}: {ex.Message}");
                return projectGid;
            }
        }
        return projectGid;
    }

    private static async Task<List<AsanaTask>> FetchTasksForProjectAsync(
        string projectGid,
        string token,
        Action<string> log,
        CancellationToken ct)
    {
        var completedSince = DateTimeOffset.UtcNow.AddDays(-7).ToString("O");
        var query = new Dictionary<string, string>
        {
            ["project"] = projectGid,
            ["opt_fields"] =
                "name,completed,completed_at,due_on,assignee,assignee.name,assignee.gid," +
                "notes,gid,projects,projects.name,followers,followers.gid,followers.name," +
                "custom_fields,custom_fields.name,custom_fields.text_value,custom_fields.enum_value,custom_fields.number_value," +
                "num_subtasks",
            ["completed_since"] = completedSince
        };
        return await FetchPagedTasksAsync("https://app.asana.com/api/1.0/tasks", query, token, log, ct);
    }

    private static async Task<List<AsanaTask>> FetchSubtasksAsync(
        string taskGid,
        string token,
        Action<string> log,
        CancellationToken ct)
    {
        var query = new Dictionary<string, string>
        {
            ["opt_fields"] = "name,completed,due_on,assignee,assignee.name,assignee.gid,gid"
        };
        return await FetchPagedTasksAsync($"https://app.asana.com/api/1.0/tasks/{taskGid}/subtasks", query, token, log, ct);
    }

    private static async Task<List<AsanaTask>> FetchPagedTasksAsync(
        string baseUrl,
        Dictionary<string, string> query,
        string token,
        Action<string> log,
        CancellationToken ct)
    {
        var all = new List<AsanaTask>();
        string? offset = null;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var url = BuildUrl(baseUrl, query, offset);
            var success = false;
            string? raw = null;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var req = new HttpRequestMessage(HttpMethod.Get, url);
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    using var resp = await Http.SendAsync(req, ct);

                    if ((int)resp.StatusCode == 429)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(2 * (attempt + 1)), ct);
                        continue;
                    }

                    raw = await resp.Content.ReadAsStringAsync(ct);
                    if (resp.IsSuccessStatusCode)
                    {
                        success = true;
                        break;
                    }

                    log($"  WARNING: Asana API request failed ({(int)resp.StatusCode})");
                    return all;
                }
                catch (Exception ex)
                {
                    if (attempt == 2)
                    {
                        log($"  WARNING: Asana API request failed: {ex.Message}");
                        return all;
                    }
                }
            }

            if (!success || string.IsNullOrWhiteSpace(raw))
                return all;

            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("data", out var data) &&
                data.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray())
                {
                    try
                    {
                        var task = JsonSerializer.Deserialize<AsanaTask>(item.GetRawText(), JsonOptions);
                        if (task != null && !string.IsNullOrWhiteSpace(task.Gid))
                            all.Add(task);
                    }
                    catch
                    {
                        // Ignore a malformed task entry and continue.
                    }
                }
            }

            offset = null;
            if (doc.RootElement.TryGetProperty("next_page", out var nextPage) &&
                nextPage.ValueKind == JsonValueKind.Object &&
                nextPage.TryGetProperty("offset", out var offsetEl))
            {
                offset = offsetEl.GetString();
            }

            if (string.IsNullOrWhiteSpace(offset))
                break;
        }

        return all;
    }

    private static string BuildUrl(string baseUrl, Dictionary<string, string> query, string? offset)
    {
        var q = new List<string>(query.Count + 1);
        foreach (var (k, v) in query)
            q.Add($"{Uri.EscapeDataString(k)}={Uri.EscapeDataString(v)}");
        if (!string.IsNullOrWhiteSpace(offset))
            q.Add($"offset={Uri.EscapeDataString(offset)}");
        return $"{baseUrl}?{string.Join("&", q)}";
    }

    private static bool IsOwnedOrCollaborating(AsanaTask task, string userGid)
        => ClassifyTaskRole(task, userGid) is "Owner" or "Collab";

    private static string ClassifyTaskRole(AsanaTask task, string userGid)
    {
        if (task.Assignee?.Gid == userGid)
            return "Owner";

        foreach (var follower in task.Followers)
        {
            if (follower.Gid == userGid)
                return "Collab";
        }
        return "Other";
    }

    private static string? GetCustomFieldValue(AsanaTask task, string fieldName)
    {
        foreach (var field in task.CustomFields)
        {
            if (field.Name != fieldName)
                continue;
            if (!string.IsNullOrWhiteSpace(field.TextValue))
                return field.TextValue;
            if (!string.IsNullOrWhiteSpace(field.EnumValue?.Name))
                return field.EnumValue.Name;
            if (field.NumberValue.HasValue)
                return field.NumberValue.Value.ToString();
        }
        return null;
    }

    private static Dictionary<string, string> LoadExistingMemos(string filePath)
    {
        var memos = new Dictionary<string, string>();
        if (!File.Exists(filePath))
            return memos;

        string[] lines;
        try { lines = File.ReadAllLines(filePath, Encoding.UTF8); }
        catch { return memos; }

        string? currentGid = null;
        var current = new StringBuilder();

        void Flush()
        {
            if (string.IsNullOrWhiteSpace(currentGid))
                return;
            memos[currentGid] = current.ToString();
            currentGid = null;
            current.Clear();
        }

        foreach (var line in lines)
        {
            var start = MemoStartRx.Match(line);
            if (start.Success)
            {
                Flush();
                currentGid = start.Groups[1].Value;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(currentGid))
            {
                if (TaskLineRx.IsMatch(line) || HeadingRx.IsMatch(line))
                {
                    Flush();
                    continue;
                }
                current.AppendLine(line);
            }
        }
        Flush();
        return memos;
    }

    private static List<AsanaTask> DeduplicateTasks(List<AsanaTask> tasks)
    {
        var seen = new Dictionary<string, AsanaTask>();
        foreach (var task in tasks)
        {
            if (string.IsNullOrWhiteSpace(task.Gid))
                continue;
            if (!seen.TryGetValue(task.Gid, out var existing))
            {
                seen[task.Gid] = task;
                continue;
            }

            var existingDue = existing.DueOn ?? "";
            var newDue = task.DueOn ?? "";
            if (string.CompareOrdinal(newDue, existingDue) > 0)
                seen[task.Gid] = task;
        }
        return seen.Values.ToList();
    }

    private static void WriteProjectFile(
        string outputPath,
        string projectName,
        List<ProjectSection> sections,
        List<AsanaTask> personalTasks,
        string userGid)
    {
        var memos = LoadExistingMemos(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        using var w = new StreamWriter(outputPath, false, new UTF8Encoding(false));
        w.WriteLine($"# Asana Tasks: {projectName}");
        w.WriteLine($"Last Sync: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        w.WriteLine();
        w.WriteLine("> このファイルは C# 実装により自動生成されます。");
        w.WriteLine("> 'Memo area' 以下の記述は保持されます。");
        w.WriteLine();

        foreach (var sec in sections)
            WriteProjectSection(w, sec.ProjectName, sec.ProjectGid, sec.Tasks, userGid, memos);

        if (personalTasks.Count > 0)
            WriteProjectSection(w, "From personal tasks", null, personalTasks, userGid, memos);
    }

    private static void WritePersonalFile(string outputPath, List<AsanaTask> tasks, string userGid)
    {
        var memos = LoadExistingMemos(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        using var w = new StreamWriter(outputPath, false, new UTF8Encoding(false));
        w.WriteLine("# Asana Tasks: Personal / Uncategorized");
        w.WriteLine($"Last Sync: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        w.WriteLine();
        w.WriteLine("> このファイルは C# 実装により自動生成されます。");
        w.WriteLine("> 'Memo area' 以下の記述は保持されます。");
        w.WriteLine();

        tasks = DeduplicateTasks(tasks);
        var inProgress = tasks.Where(t => !t.Completed).OrderBy(t => TaskSortKey(t, userGid)).ToList();
        var completed = tasks.Where(t => t.Completed).ToList();

        w.WriteLine("## In Progress");
        w.WriteLine();
        if (inProgress.Count > 0)
        {
            foreach (var task in inProgress)
                WriteTaskLine(w, task, ClassifyTaskRole(task, userGid), memos, userGid);
        }
        else
        {
            w.WriteLine("(no tasks)");
            w.WriteLine();
        }

        w.WriteLine("## Completed (recent)");
        w.WriteLine();
        if (completed.Count > 0)
        {
            foreach (var task in completed)
                WriteTaskLine(w, task, ClassifyTaskRole(task, userGid), memos, userGid);
        }
        else
        {
            w.WriteLine("(no tasks)");
        }
    }

    private static void WriteWorkstreamFile(
        string outputPath,
        string projectName,
        string workstreamId,
        List<AsanaTask> tasks,
        string userGid)
    {
        var memos = LoadExistingMemos(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        using var w = new StreamWriter(outputPath, false, new UTF8Encoding(false));
        w.WriteLine($"# Asana Tasks: {projectName} / {workstreamId}");
        w.WriteLine($"Last Sync: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        w.WriteLine();
        w.WriteLine("> This file is auto-generated by Asana Sync.");
        w.WriteLine("> Memo areas are preserved.");
        w.WriteLine();

        tasks = DeduplicateTasks(tasks);
        var inProgress = tasks.Where(t => !t.Completed).OrderBy(t => TaskSortKey(t, userGid)).ToList();
        var completed = tasks.Where(t => t.Completed).ToList();

        w.WriteLine("## In Progress");
        w.WriteLine();
        if (inProgress.Count > 0)
        {
            foreach (var task in inProgress)
                WriteTaskLine(w, task, ClassifyTaskRole(task, userGid), memos, userGid);
        }
        else
        {
            w.WriteLine("(no tasks)");
            w.WriteLine();
        }

        w.WriteLine("## Completed (recent)");
        w.WriteLine();
        if (completed.Count > 0)
        {
            foreach (var task in completed)
                WriteTaskLine(w, task, ClassifyTaskRole(task, userGid), memos, userGid);
        }
        else
        {
            w.WriteLine("(no tasks)");
        }
    }

    private static SplitResult SplitWorkstreamTasks(ProjectData projData)
    {
        var rootSections = new List<ProjectSection>();
        var rootPersonal = new List<AsanaTask>();
        var wsTasks = new Dictionary<string, List<AsanaTask>>(StringComparer.OrdinalIgnoreCase);

        foreach (var sec in projData.Sections)
        {
            var rootTasks = new List<AsanaTask>();
            foreach (var task in sec.Tasks)
            {
                var wsId = ResolveWorkstreamId(task, projData.Project);
                if (string.IsNullOrWhiteSpace(wsId))
                {
                    rootTasks.Add(task);
                    continue;
                }

                if (!wsTasks.TryGetValue(wsId, out var list))
                {
                    list = [];
                    wsTasks[wsId] = list;
                }
                list.Add(task);
            }

            if (rootTasks.Count > 0)
                rootSections.Add(new ProjectSection(sec.ProjectName, sec.ProjectGid, rootTasks));
        }

        foreach (var task in projData.PersonalTasks)
        {
            var wsId = ResolveWorkstreamId(task, projData.Project);
            if (string.IsNullOrWhiteSpace(wsId))
            {
                rootPersonal.Add(task);
                continue;
            }

            if (!wsTasks.TryGetValue(wsId, out var list))
            {
                list = [];
                wsTasks[wsId] = list;
            }
            list.Add(task);
        }

        return new SplitResult(rootSections, rootPersonal, wsTasks);
    }

    private static string? ResolveWorkstreamId(AsanaTask task, DiscoveredProject project)
    {
        // 1) タスク側 custom field (例: workstream-id) を優先
        var fromField = GetCustomFieldValue(task, project.WorkstreamFieldName);
        var normalizedFromField = NormalizeWorkstreamId(fromField);
        if (!string.IsNullOrWhiteSpace(normalizedFromField))
            return normalizedFromField;

        // 2) Asana project gid -> workstream-id マッピング
        if (!string.IsNullOrWhiteSpace(task.SourceProjectGid)
            && project.WorkstreamProjectMap.TryGetValue(task.SourceProjectGid, out var mapped))
        {
            return NormalizeWorkstreamId(mapped);
        }

        return null;
    }

    private static string? NormalizeWorkstreamId(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim().ToLowerInvariant().Replace('_', '-').Replace(' ', '-');
        s = Regex.Replace(s, "-{2,}", "-").Trim('-');
        return WorkstreamIdRx.IsMatch(s) ? s : null;
    }

    private static void WriteGlobalSummary(
        string outputPath,
        List<SummaryProjectData> allProjects,
        List<AsanaTask> personalTasks,
        string userGid)
    {
        var memos = LoadExistingMemos(outputPath);
        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDir))
            Directory.CreateDirectory(outputDir);

        using var w = new StreamWriter(outputPath, false, new UTF8Encoding(false));
        w.WriteLine("# Asana Tasks View (All Projects)");
        w.WriteLine($"Last Sync: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        w.WriteLine("> このファイルは C# 実装により自動生成されます。");
        w.WriteLine("> 各案件の詳細は個別の asana-tasks.md を参照してください。");
        w.WriteLine("> 'Memo area' 以下の記述は保持されます。");
        w.WriteLine();

        w.WriteLine("## 目次");
        w.WriteLine();
        foreach (var p in allProjects)
        {
            var inProgressCount = p.Sections.Sum(s => s.Tasks.Count(t => !t.Completed))
                + p.PersonalTasks.Count(t => !t.Completed);
            var anchor = p.ProjectName.Replace(" ", "-")
                .Replace("(", "").Replace(")", "")
                .Replace("[", "").Replace("]", "");
            w.WriteLine($"- [{p.ProjectName}](#{anchor}) (In Progress: {inProgressCount})");
        }
        if (personalTasks.Count > 0)
            w.WriteLine($"- [Personal / Uncategorized](#personal--uncategorized) (In Progress: {personalTasks.Count(t => !t.Completed)})");

        w.WriteLine();
        w.WriteLine("---");
        w.WriteLine();

        foreach (var p in allProjects)
        {
            w.WriteLine($"## {p.ProjectName}");
            w.WriteLine();

            var allTasks = new List<AsanaTask>();
            foreach (var sec in p.Sections) allTasks.AddRange(sec.Tasks);
            allTasks.AddRange(p.PersonalTasks);
            allTasks = DeduplicateTasks(allTasks);

            var inProgress = allTasks.Where(t => !t.Completed).OrderBy(t => TaskSortKey(t, userGid)).ToList();
            if (inProgress.Count > 0)
            {
                foreach (var task in inProgress)
                    WriteTaskLine(w, task, ClassifyTaskRole(task, userGid), memos, userGid);
            }
            else
            {
                w.WriteLine("(タスクなし)");
                w.WriteLine();
            }

            w.WriteLine();
        }

        if (personalTasks.Count > 0)
        {
            w.WriteLine("## Personal / Uncategorized");
            w.WriteLine();
            var inProgress = personalTasks.Where(t => !t.Completed)
                .OrderBy(t => TaskSortKey(t, userGid))
                .ToList();
            if (inProgress.Count > 0)
            {
                foreach (var task in inProgress)
                    WriteTaskLine(w, task, ClassifyTaskRole(task, userGid), memos, userGid);
            }
            else
            {
                w.WriteLine("(タスクなし)");
            }
            w.WriteLine();
        }
    }

    private static void WriteProjectSection(
        StreamWriter w,
        string projectName,
        string? projectGid,
        List<AsanaTask> tasks,
        string userGid,
        Dictionary<string, string> memos)
    {
        if (!string.IsNullOrWhiteSpace(projectGid))
            w.WriteLine($"## [{projectName}](https://app.asana.com/0/{projectGid}/list)");
        else
            w.WriteLine($"## {projectName}");
        w.WriteLine();

        tasks = DeduplicateTasks(tasks);
        var inProgress = tasks.Where(t => !t.Completed).OrderBy(t => TaskSortKey(t, userGid)).ToList();
        var completed = tasks.Where(t => t.Completed).ToList();

        w.WriteLine("### In Progress");
        w.WriteLine();
        if (inProgress.Count > 0)
        {
            foreach (var task in inProgress)
                WriteTaskLine(w, task, ClassifyTaskRole(task, userGid), memos, userGid);
        }
        else
        {
            w.WriteLine("(no tasks)");
            w.WriteLine();
        }

        w.WriteLine("### Completed (recent)");
        w.WriteLine();
        if (completed.Count > 0)
        {
            foreach (var task in completed)
                WriteTaskLine(w, task, ClassifyTaskRole(task, userGid), memos, userGid);
        }
        else
        {
            w.WriteLine("(no tasks)");
        }
        w.WriteLine();
    }

    private static void WriteTaskLine(
        StreamWriter w,
        AsanaTask task,
        string role,
        Dictionary<string, string> existingMemos,
        string userGid = "")
    {
        var gid = task.Gid;
        var check = task.Completed ? "x" : " ";
        var due = string.IsNullOrWhiteSpace(task.DueOn) ? "" : $" (Due: {task.DueOn})";
        var roleTag = string.IsNullOrWhiteSpace(role) ? "" : $"[{role}] ";
        var anken = GetCustomFieldValue(task, "Project");
        var ankenTag = string.IsNullOrWhiteSpace(anken) ? "" : $"[{anken}] ";
        var priority = !task.Completed ? GetCustomFieldValue(task, "Priority") : null;
        var priorityTag = string.IsNullOrWhiteSpace(priority) ? "" : $" [{priority}]";
        var completedTag = "";
        if (task.Completed && !string.IsNullOrWhiteSpace(task.CompletedAt)
            && DateTime.TryParse(task.CompletedAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var completedDt))
        {
            completedTag = $" <!-- completed: {completedDt.ToLocalTime():yyyy-MM-dd} -->";
        }
        w.WriteLine($"- [{check}] {roleTag}{ankenTag}{task.Name}{due}{priorityTag} [[Asana](https://app.asana.com/0/0/{gid})]{completedTag}");

        var notes = (task.Notes ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(notes))
        {
            var lines = notes.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var maxLines = task.Completed ? 3 : 10;
            if (lines.Length > maxLines)
                lines = [.. lines.Take(maxLines), "..."];

            foreach (var line in lines)
                w.WriteLine(string.IsNullOrEmpty(line) ? "    >" : $"    > {line}");
        }

        if (task.SubtasksData.Count > 0)
        {
            foreach (var sub in task.SubtasksData)
            {
                var subCheck = sub.Completed ? "x" : " ";
                var subDue = string.IsNullOrWhiteSpace(sub.DueOn) ? "" : $" (Due: {sub.DueOn})";
                var subRole = string.IsNullOrWhiteSpace(userGid) ? "" : ClassifyTaskRole(sub, userGid);
                var subRoleTag = string.IsNullOrWhiteSpace(subRole) ? "" : $"[{subRole}] ";
                w.WriteLine($"    - [{subCheck}] {subRoleTag}{sub.Name}{subDue} [[Asana](https://app.asana.com/0/0/{sub.Gid})]");
            }
        }

        if (!task.Completed)
        {
            w.WriteLine($"    - <!-- Memo area for {gid} -->");
            if (existingMemos.TryGetValue(gid, out var memo) && !string.IsNullOrWhiteSpace(memo))
            {
                w.Write(memo);
                if (!memo.EndsWith('\n'))
                    w.WriteLine();
            }
            else
            {
                w.WriteLine();
            }
        }
    }

    private static (int roleOrder, string due) TaskSortKey(AsanaTask task, string userGid)
    {
        var roleOrder = ClassifyTaskRole(task, userGid) switch
        {
            "Owner" => 0,
            "Collab" => 1,
            _ => 2
        };
        var due = string.IsNullOrWhiteSpace(task.DueOn) ? "9999-99-99" : task.DueOn;
        return (roleOrder, due);
    }

    private sealed class AsanaProjectConfig
    {
        [JsonPropertyName("asana_project_gids")]
        public List<string> AsanaProjectGids { get; set; } = [];

        [JsonPropertyName("anken_aliases")]
        public List<string> AnkenAliases { get; set; } = [];

        [JsonPropertyName("workstream_project_map")]
        public Dictionary<string, string> WorkstreamProjectMap { get; set; } = [];

        [JsonPropertyName("workstream_field_name")]
        public string WorkstreamFieldName { get; set; } = "workstream-id";
    }

    private sealed class AsanaTask
    {
        [JsonPropertyName("gid")]
        public string Gid { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("completed")]
        public bool Completed { get; set; }

        [JsonPropertyName("completed_at")]
        public string? CompletedAt { get; set; }  // "2026-03-20T12:00:00.000Z" or null

        [JsonPropertyName("due_on")]
        public string? DueOn { get; set; }

        [JsonPropertyName("assignee")]
        public AsanaUser? Assignee { get; set; }

        [JsonPropertyName("followers")]
        public List<AsanaUser> Followers { get; set; } = [];

        [JsonPropertyName("notes")]
        public string? Notes { get; set; }

        [JsonPropertyName("custom_fields")]
        public List<AsanaCustomField> CustomFields { get; set; } = [];

        [JsonPropertyName("num_subtasks")]
        public int NumSubtasks { get; set; }

        public string? SourceProjectGid { get; set; }
        public List<AsanaTask> SubtasksData { get; set; } = [];
    }

    private sealed class AsanaUser
    {
        [JsonPropertyName("gid")]
        public string Gid { get; set; } = "";
    }

    private sealed class AsanaCustomField
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("text_value")]
        public string? TextValue { get; set; }

        [JsonPropertyName("enum_value")]
        public AsanaEnumValue? EnumValue { get; set; }

        [JsonPropertyName("number_value")]
        public decimal? NumberValue { get; set; }
    }

    private sealed class AsanaEnumValue
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    private sealed record DiscoveredProject(
        string Name,
        string BoxPath,
        string RelativePath,
        List<string> AsanaProjectGids,
        List<string> AnkenAliases,
        Dictionary<string, string> WorkstreamProjectMap,
        string WorkstreamFieldName,
        bool HasAsanaConfigFile);
    private sealed record SplitResult(
        List<ProjectSection> RootSections,
        List<AsanaTask> RootPersonalTasks,
        Dictionary<string, List<AsanaTask>> WorkstreamTasks);

    private sealed class ProjectData
    {
        public DiscoveredProject Project { get; }
        public List<ProjectSection> Sections { get; }
        public List<AsanaTask> PersonalTasks { get; }

        public ProjectData(DiscoveredProject project, List<ProjectSection> sections, List<AsanaTask> personalTasks)
        {
            Project = project;
            Sections = sections;
            PersonalTasks = personalTasks;
        }
    }

    private sealed record ProjectSection(string ProjectName, string ProjectGid, List<AsanaTask> Tasks);
    private sealed record SummaryProjectData(string ProjectName, List<ProjectSection> Sections, List<AsanaTask> PersonalTasks);
}
