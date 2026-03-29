using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using ProjectCurator.Models;

namespace ProjectCurator.Services;

public class AgentDeploymentService
{
    private readonly ConfigService _configService;

    public AgentDeploymentService(ConfigService configService)
    {
        _configService = configService;
    }

    // ─── Agent Deploy/Undeploy ────────────────────────────────────────────

    public void DeployAgent(ProjectInfo project, string targetSubPath, AgentDefinition def, string content, CliTarget cli)
    {
        var normalizedSubPath = NormalizeTargetSubPath(targetSubPath);
        var writeDir = ResolveAgentWriteDir(project.Path, normalizedSubPath, cli);
        try
        {
            Directory.CreateDirectory(writeDir);
            var filePath = ResolveAgentFilePath(writeDir, def, cli);
            var normalizedContent = NormalizeAgentContent(def, content, cli);
            File.WriteAllText(filePath, normalizedContent, new UTF8Encoding(false));
            Debug.WriteLine($"[AgentDeploy] '{def.Name}' → {filePath}");
            UpdateAgentState(project.Name, normalizedSubPath, def.Id, cli, deployed: true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AgentDeploy] Failed: {ex.Message}");
            throw;
        }
    }

    public void UndeployAgent(ProjectInfo project, string targetSubPath, AgentDefinition def, CliTarget cli)
    {
        var normalizedSubPath = NormalizeTargetSubPath(targetSubPath);
        var writeDir = ResolveAgentWriteDir(project.Path, normalizedSubPath, cli);
        var filePath = ResolveAgentFilePath(writeDir, def, cli);
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                Debug.WriteLine($"[AgentUndeploy] '{def.Name}' ← {filePath}");
            }
            UpdateAgentState(project.Name, normalizedSubPath, def.Id, cli, deployed: false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AgentUndeploy] Failed: {ex.Message}");
            throw;
        }
    }

    public bool IsAgentDeployed(ProjectInfo project, string targetSubPath, AgentDefinition def, CliTarget cli)
    {
        var writeDir = ResolveAgentWriteDir(project.Path, NormalizeTargetSubPath(targetSubPath), cli);
        return File.Exists(ResolveAgentFilePath(writeDir, def, cli));
    }

    // ─── Rule Deploy/Undeploy ─────────────────────────────────────────────

    public void DeployRule(ProjectInfo project, string targetSubPath, ContextRuleDefinition def, string content, CliTarget cli)
    {
        var normalizedSubPath = NormalizeTargetSubPath(targetSubPath);
        var contextFilePaths = ResolveRuleContextFilePaths(project.Path, normalizedSubPath, cli);
        if (contextFilePaths.Count == 0) return;

        try
        {
            foreach (var contextFilePath in contextFilePaths)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(contextFilePath)!);

                var existing = File.Exists(contextFilePath)
                    ? File.ReadAllText(contextFilePath, new UTF8Encoding(false))
                    : "";

                var markerStart = $"<!-- [AgentHub:{def.Id}] -->";
                var newSection = $"{markerStart}\n{content.TrimEnd()}\n<!-- [/AgentHub:{def.Id}] -->";

                string newContent;
                if (existing.Contains(markerStart))
                    newContent = ReplaceMarkedSection(existing, def.Id, newSection);
                else
                    newContent = existing.TrimEnd() + "\n\n" + newSection + "\n";

                File.WriteAllText(contextFilePath, newContent, new UTF8Encoding(false));
            }

            UpdateRuleState(project.Name, normalizedSubPath, def.Id, cli, deployed: true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RuleDeploy] Failed: {ex.Message}");
            throw;
        }
    }

    public void UndeployRule(ProjectInfo project, string targetSubPath, ContextRuleDefinition def, CliTarget cli)
    {
        var normalizedSubPath = NormalizeTargetSubPath(targetSubPath);
        var contextFilePaths = ResolveRuleContextFilePaths(project.Path, normalizedSubPath, cli);
        if (contextFilePaths.Count == 0) return;

        try
        {
            foreach (var contextFilePath in contextFilePaths)
            {
                if (!File.Exists(contextFilePath))
                    continue;

                var existing = File.ReadAllText(contextFilePath, new UTF8Encoding(false));
                var markerStart = $"<!-- [AgentHub:{def.Id}] -->";
                if (!existing.Contains(markerStart))
                    continue;

                var newContent = RemoveMarkedSection(existing, def.Id);
                File.WriteAllText(contextFilePath, newContent, new UTF8Encoding(false));
            }

            UpdateRuleState(project.Name, normalizedSubPath, def.Id, cli, deployed: false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RuleUndeploy] Failed: {ex.Message}");
            throw;
        }
    }

    public bool IsRuleDeployed(ProjectInfo project, string targetSubPath, ContextRuleDefinition def, CliTarget cli)
    {
        var contextFilePaths = ResolveRuleContextFilePaths(project.Path, NormalizeTargetSubPath(targetSubPath), cli);
        if (contextFilePaths.Count == 0) return false;

        foreach (var contextFilePath in contextFilePaths)
        {
            if (!File.Exists(contextFilePath))
                return false;

            try
            {
                var content = File.ReadAllText(contextFilePath, new UTF8Encoding(false));
                if (!content.Contains($"<!-- [AgentHub:{def.Id}] -->"))
                    return false;
            }
            catch
            {
                return false;
            }
        }

        return true;
    }

    // ─── State Sync ───────────────────────────────────────────────────────

    public void SyncDeploymentState(
        List<AgentDefinition> agentDefs,
        List<ContextRuleDefinition> ruleDefs,
        List<ProjectInfo> projects)
    {
        var state = _configService.LoadAgentHubState();
        bool changed = false;

        var validAgents = new List<AgentDeployment>();
        foreach (var dep in state.AgentDeployments)
        {
            var project = projects.FirstOrDefault(p => p.Name == dep.ProjectName);
            var agentDef = agentDefs.FirstOrDefault(a => a.Id == dep.AgentId);
            if (project == null || agentDef == null) { changed = true; continue; }

            var validClis = dep.CliTargets
                .Where(cli => IsAgentDeployed(project, dep.TargetSubPath, agentDef, cli))
                .ToList();

            if (validClis.Count != dep.CliTargets.Count) changed = true;
            if (validClis.Count > 0)
                validAgents.Add(new AgentDeployment
                {
                    ProjectName = dep.ProjectName,
                    TargetSubPath = dep.TargetSubPath,
                    AgentId = dep.AgentId,
                    CliTargets = validClis,
                    DeployedAt = dep.DeployedAt
                });
            else
                changed = true;
        }

        var validRules = new List<RuleDeployment>();
        foreach (var dep in state.RuleDeployments)
        {
            var project = projects.FirstOrDefault(p => p.Name == dep.ProjectName);
            var ruleDef = ruleDefs.FirstOrDefault(r => r.Id == dep.RuleId);
            if (project == null || ruleDef == null) { changed = true; continue; }

            var validClis = dep.CliTargets
                .Where(cli => IsRuleDeployed(project, dep.TargetSubPath, ruleDef, cli))
                .ToList();

            if (validClis.Count != dep.CliTargets.Count) changed = true;
            if (validClis.Count > 0)
                validRules.Add(new RuleDeployment
                {
                    ProjectName = dep.ProjectName,
                    TargetSubPath = dep.TargetSubPath,
                    RuleId = dep.RuleId,
                    CliTargets = validClis,
                    DeployedAt = dep.DeployedAt
                });
            else
                changed = true;
        }

        if (changed)
        {
            state.AgentDeployments = validAgents;
            state.RuleDeployments = validRules;
            _configService.SaveAgentHubState(state);
        }
    }

    // ─── Path Resolution ─────────────────────────────────────────────────

    public string ResolveAgentWriteDir(string projectPath, string targetSubPath, CliTarget cli)
    {
        var normalizedSubPath = NormalizeTargetSubPath(targetSubPath);
        var targetDir = string.IsNullOrEmpty(normalizedSubPath)
            ? projectPath
            : Path.Combine(projectPath, normalizedSubPath);

        if (cli == CliTarget.Copilot)
            return Path.Combine(targetDir, ".github", "agents");

        var cliDirName = cli switch
        {
            CliTarget.Claude => ".claude",
            CliTarget.Codex => ".codex",
            CliTarget.Gemini => ".gemini",
            _ => ".claude"
        };

        var localCliDir = Path.Combine(targetDir, cliDirName);
        var resolved = ResolveJunctionTarget(localCliDir);
        return Path.Combine(resolved ?? localCliDir, "agents");
    }

    private static string? ResolveContextFilePath(string projectPath, string targetSubPath, CliTarget cli)
    {
        var normalizedSubPath = NormalizeTargetSubPath(targetSubPath);
        var targetDir = string.IsNullOrEmpty(normalizedSubPath)
            ? projectPath
            : Path.Combine(projectPath, normalizedSubPath);

        return cli switch
        {
            CliTarget.Claude => Path.Combine(targetDir, "CLAUDE.md"),
            CliTarget.Codex => Path.Combine(targetDir, "AGENTS.md"),
            CliTarget.Copilot => Path.Combine(targetDir, ".github", "copilot-instructions.md"),
            CliTarget.Gemini => Path.Combine(targetDir, "GEMINI.md"),
            _ => null
        };
    }

    private static List<string> ResolveRuleContextFilePaths(string projectPath, string targetSubPath, CliTarget cli)
    {
        var result = new List<string>();
        var primary = ResolveContextFilePath(projectPath, targetSubPath, cli);
        if (!string.IsNullOrWhiteSpace(primary))
            result.Add(primary);

        // Shared mirror: when target is project root, apply same rule file to shared side.
        if (string.IsNullOrEmpty(targetSubPath))
        {
            var sharedDir = Path.Combine(projectPath, "shared");
            if (Directory.Exists(sharedDir))
            {
                var sharedPath = ResolveContextFilePath(projectPath, "shared", cli);
                if (!string.IsNullOrWhiteSpace(sharedPath) &&
                    !result.Contains(sharedPath, StringComparer.OrdinalIgnoreCase))
                {
                    result.Add(sharedPath);
                }
            }
        }

        return result;
    }

    private static string? ResolveJunctionTarget(string path)
    {
        try
        {
            var attr = File.GetAttributes(path);
            if ((attr & FileAttributes.ReparsePoint) != 0)
            {
                var target = Directory.ResolveLinkTarget(path, returnFinalTarget: false);
                if (target != null)
                    return Path.GetFullPath(target.FullName);
            }
        }
        catch { }
        return null;
    }

    private static string NormalizeTargetSubPath(string? targetSubPath)
    {
        if (string.IsNullOrWhiteSpace(targetSubPath))
            return "";
        var normalized = targetSubPath.Replace('/', '\\').Trim();
        while (normalized.StartsWith('\\'))
            normalized = normalized[1..];
        return normalized.TrimEnd('\\');
    }

    private static string NormalizeAgentContent(AgentDefinition def, string content, CliTarget cli)
    {
        return cli == CliTarget.Codex
            ? NormalizeAgentContentForCodex(def, content)
            : NormalizeAgentContentForMarkdownFrontmatter(def, content, cli);
    }

    private static string NormalizeAgentContentForMarkdownFrontmatter(AgentDefinition def, string content, CliTarget cli)
    {
        var trimmed = content.TrimStart();
        var requiredName = ToKebabCase(string.IsNullOrWhiteSpace(def.Id) ? def.Name : def.Id);
        var requiredDescription = string.IsNullOrWhiteSpace(def.Description)
            ? "ProjectCurator managed sub-agent."
            : def.Description.Trim();
        var extraFrontmatter = NormalizeFrontmatterLines(GetFrontmatterByCli(def, cli));

        if (trimmed.StartsWith("---\n", StringComparison.Ordinal) || trimmed.StartsWith("---\r\n", StringComparison.Ordinal))
        {
            return EnsureRequiredFrontmatter(trimmed, requiredName, requiredDescription, extraFrontmatter);
        }

        var yamlName = EscapeYamlScalar(requiredName);
        var yamlDescription = EscapeYamlScalar(requiredDescription);
        var extraBlock = string.IsNullOrWhiteSpace(extraFrontmatter) ? "" : $"\n{extraFrontmatter}";

        return
            $"---\nname: \"{yamlName}\"\ndescription: \"{yamlDescription}\"{extraBlock}\n---\n\n{content.TrimStart()}";
    }

    private static string NormalizeAgentContentForCodex(AgentDefinition def, string content)
    {
        var name = ToKebabCase(string.IsNullOrWhiteSpace(def.Id) ? def.Name : def.Id);
        var description = string.IsNullOrWhiteSpace(def.Description)
            ? "ProjectCurator managed sub-agent."
            : def.Description.Trim();
        var extraLines = NormalizeTomlLines(GetFrontmatterByCli(def, CliTarget.Codex));

        var lines = new List<string>
        {
            $"name = \"{EscapeTomlString(name)}\"",
            $"description = \"{EscapeTomlString(description)}\""
        };

        foreach (var line in extraLines)
        {
            var key = ExtractTomlKey(line);
            if (string.Equals(key, "name", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(key, "description", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(key, "developer_instructions", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            lines.Add(line);
        }

        lines.Add($"developer_instructions = \"\"\"\n{content.Trim()}\n\"\"\"");
        return string.Join("\n", lines) + "\n";
    }

    private static string EscapeYamlScalar(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static string EscapeTomlString(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static string EnsureRequiredFrontmatter(
        string contentWithFrontmatter,
        string requiredName,
        string requiredDescription,
        string extraFrontmatter)
    {
        var newline = contentWithFrontmatter.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var match = Regex.Match(
            contentWithFrontmatter,
            @"\A---\r?\n(?<fm>[\s\S]*?)\r?\n---\r?\n?",
            RegexOptions.CultureInvariant);
        if (!match.Success)
            return contentWithFrontmatter;

        var frontmatter = match.Groups["fm"].Value;
        var body = contentWithFrontmatter[match.Length..];

        var hasName = Regex.IsMatch(frontmatter, @"(?im)^\s*name\s*:");
        var hasDescription = Regex.IsMatch(frontmatter, @"(?im)^\s*description\s*:");
        if (!hasName)
            frontmatter += $"{newline}name: \"{EscapeYamlScalar(requiredName)}\"";
        if (!hasDescription)
            frontmatter += $"{newline}description: \"{EscapeYamlScalar(requiredDescription)}\"";
        if (!string.IsNullOrWhiteSpace(extraFrontmatter))
        {
            foreach (var line in extraFrontmatter.Split('\n'))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed))
                    continue;
                var keyMatch = Regex.Match(trimmed, @"^(?<k>[A-Za-z][A-Za-z0-9_-]*)\s*:");
                if (keyMatch.Success)
                {
                    var key = keyMatch.Groups["k"].Value;
                    var exists = Regex.IsMatch(frontmatter, $@"(?im)^\s*{Regex.Escape(key)}\s*:");
                    if (exists)
                        continue;
                }
                frontmatter += $"{newline}{trimmed}";
            }
        }

        return $"---{newline}{frontmatter.TrimEnd()}{newline}---{newline}{newline}{body.TrimStart()}";
    }

    private static string NormalizeFrontmatterLines(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "";

        var lines = raw
            .Replace("\r\n", "\n")
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l) && l != "---")
            .ToList();

        return string.Join("\n", lines);
    }

    private static string GetAgentFileBaseName(AgentDefinition def)
    {
        var raw = string.IsNullOrWhiteSpace(def.Id) ? def.Name : def.Id;
        var kebab = ToKebabCase(raw);
        return string.IsNullOrWhiteSpace(kebab) ? "agent" : kebab;
    }

    private static string ToKebabCase(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "agent";
        var kebab = Regex.Replace(value.Trim().ToLowerInvariant(), @"[^a-z0-9]+", "-");
        kebab = Regex.Replace(kebab, "-{2,}", "-").Trim('-');
        return string.IsNullOrWhiteSpace(kebab) ? "agent" : kebab;
    }

    private static string ResolveAgentFilePath(string writeDir, AgentDefinition def, CliTarget cli)
    {
        var ext = cli == CliTarget.Codex ? ".toml" : ".md";
        return Path.Combine(writeDir, GetAgentFileBaseName(def) + ext);
    }

    private static List<string> NormalizeTomlLines(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        return
        [
            .. raw.Replace("\r\n", "\n")
                  .Split('\n')
                  .Select(l => l.Trim())
                  .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith('#'))
        ];
    }

    private static string ExtractTomlKey(string line)
    {
        var idx = line.IndexOf('=');
        if (idx <= 0) return "";
        return line[..idx].Trim();
    }

    private static string GetFrontmatterByCli(AgentDefinition def, CliTarget cli)
    {
        var value = cli switch
        {
            CliTarget.Claude => def.FrontmatterClaude,
            CliTarget.Codex => def.FrontmatterCodex,
            CliTarget.Copilot => def.FrontmatterCopilot,
            CliTarget.Gemini => def.FrontmatterGemini,
            _ => ""
        };

        // Backward compatibility with existing single-field config.
        if (string.IsNullOrWhiteSpace(value))
            value = def.FrontmatterYaml;

        return value ?? "";
    }

    // ─── State Management ─────────────────────────────────────────────────

    private void UpdateAgentState(string projectName, string targetSubPath, string agentId, CliTarget cli, bool deployed)
    {
        var state = _configService.LoadAgentHubState();
        var existing = state.AgentDeployments
            .FirstOrDefault(d => d.ProjectName == projectName
                              && d.TargetSubPath == targetSubPath
                              && d.AgentId == agentId);

        if (deployed)
        {
            if (existing == null)
                state.AgentDeployments.Add(new AgentDeployment
                {
                    ProjectName = projectName,
                    TargetSubPath = targetSubPath,
                    AgentId = agentId,
                    CliTargets = [cli],
                    DeployedAt = DateTimeOffset.Now
                });
            else if (!existing.CliTargets.Contains(cli))
                existing.CliTargets.Add(cli);
        }
        else
        {
            if (existing != null)
            {
                existing.CliTargets.Remove(cli);
                if (existing.CliTargets.Count == 0)
                    state.AgentDeployments.Remove(existing);
            }
        }
        _configService.SaveAgentHubState(state);
    }

    private void UpdateRuleState(string projectName, string targetSubPath, string ruleId, CliTarget cli, bool deployed)
    {
        var state = _configService.LoadAgentHubState();
        var existing = state.RuleDeployments
            .FirstOrDefault(d => d.ProjectName == projectName
                              && d.TargetSubPath == targetSubPath
                              && d.RuleId == ruleId);

        if (deployed)
        {
            if (existing == null)
                state.RuleDeployments.Add(new RuleDeployment
                {
                    ProjectName = projectName,
                    TargetSubPath = targetSubPath,
                    RuleId = ruleId,
                    CliTargets = [cli],
                    DeployedAt = DateTimeOffset.Now
                });
            else if (!existing.CliTargets.Contains(cli))
                existing.CliTargets.Add(cli);
        }
        else
        {
            if (existing != null)
            {
                existing.CliTargets.Remove(cli);
                if (existing.CliTargets.Count == 0)
                    state.RuleDeployments.Remove(existing);
            }
        }
        _configService.SaveAgentHubState(state);
    }

    // ─── Marker Helpers ───────────────────────────────────────────────────

    private static string ReplaceMarkedSection(string content, string id, string newSection)
    {
        var openMarker = Regex.Escape($"<!-- [AgentHub:{id}] -->");
        var closeMarker = Regex.Escape($"<!-- [/AgentHub:{id}] -->");
        // Use (\r?\n)+ to handle CRLF and any number of preceding newlines (not just \n\n)
        var pattern = $@"(\r?\n)+[ \t]*{openMarker}[\s\S]*?{closeMarker}[ \t]*";
        var result = Regex.Replace(content, pattern, "\n\n" + newSection);
        return result.TrimEnd() + "\n";
    }

    private static string RemoveMarkedSection(string content, string id)
    {
        var openMarker = Regex.Escape($"<!-- [AgentHub:{id}] -->");
        var closeMarker = Regex.Escape($"<!-- [/AgentHub:{id}] -->");
        // Use (\r?\n)+ to handle CRLF and any number of preceding newlines (not just \n\n).
        // Replace the matched block (including its preceding newlines) with a single \n
        // so that text before and after the section remains separated by a line break.
        var pattern = $@"(\r?\n)+[ \t]*{openMarker}[\s\S]*?{closeMarker}[ \t]*(\r?\n)?";
        var result = Regex.Replace(content, pattern, "\n");
        return result.TrimEnd() + "\n";
    }
}
