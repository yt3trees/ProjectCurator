using System.Text.Json.Serialization;

namespace ProjectCurator.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CliTarget
{
    Claude,
    Codex,
    Copilot,
    Gemini
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DeploymentScopeType
{
    Project,
    Global
}

public class AgentDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string FrontmatterYaml { get; set; } = "";
    public string FrontmatterClaude { get; set; } = "";
    public string FrontmatterCodex { get; set; } = "";
    public string FrontmatterCopilot { get; set; } = "";
    public string FrontmatterGemini { get; set; } = "";
    public string ContentFile { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
}

public class ContextRuleDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string ContentFile { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
}

public class AgentDeployment
{
    public string ProjectName { get; set; } = "";
    public string TargetSubPath { get; set; } = "";
    public DeploymentScopeType ScopeType { get; set; } = DeploymentScopeType.Project;
    public string ScopeId { get; set; } = "";
    public string AgentId { get; set; } = "";
    public List<CliTarget> CliTargets { get; set; } = [];
    public DateTimeOffset DeployedAt { get; set; } = DateTimeOffset.Now;
}

public class RuleDeployment
{
    public string ProjectName { get; set; } = "";
    public string TargetSubPath { get; set; } = "";
    public DeploymentScopeType ScopeType { get; set; } = DeploymentScopeType.Project;
    public string ScopeId { get; set; } = "";
    public string RuleId { get; set; } = "";
    public List<CliTarget> CliTargets { get; set; } = [];
    public DateTimeOffset DeployedAt { get; set; } = DateTimeOffset.Now;
}

public class SkillDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsBuiltIn { get; set; } = false;
    public string ContentDirectory { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
}

public class SkillDeployment
{
    public string ProjectName { get; set; } = "";
    public string TargetSubPath { get; set; } = "";
    public DeploymentScopeType ScopeType { get; set; } = DeploymentScopeType.Project;
    public string ScopeId { get; set; } = "";
    public string SkillId { get; set; } = "";
    public List<CliTarget> CliTargets { get; set; } = [];
    public DateTimeOffset DeployedAt { get; set; } = DateTimeOffset.Now;
}

public class GlobalDeploymentProfile
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    public string ClaudeBasePath { get; set; } = "";
    public string CodexBasePath { get; set; } = "";
    public string CopilotBasePath { get; set; } = "";
    public string GeminiBasePath { get; set; } = "";

    public string ClaudeRuleFilePath { get; set; } = "";
    public string CodexRuleFilePath { get; set; } = "";
    public string CopilotRuleFilePath { get; set; } = "";
    public string GeminiRuleFilePath { get; set; } = "";

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
}

public class GlobalDeploymentProfilesConfig
{
    public List<GlobalDeploymentProfile> Profiles { get; set; } = [];
}

public class AgentHubState
{
    public List<AgentDeployment> AgentDeployments { get; set; } = [];
    public List<RuleDeployment> RuleDeployments { get; set; } = [];
    public List<SkillDeployment> SkillDeployments { get; set; } = [];
}
