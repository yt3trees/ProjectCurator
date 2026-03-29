using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using ProjectCurator.Models;
using ProjectCurator.Services;

namespace ProjectCurator.ViewModels;

// ─── Item ViewModels ──────────────────────────────────────────────────────────

public partial class DeployAgentItemViewModel : ObservableObject
{
    private bool _suppressCallback;

    public AgentDefinition Definition { get; }
    public string Name => Definition.Name;

    public Action<DeployAgentItemViewModel, CliTarget, bool>? OnCliToggled;

    [ObservableProperty] private bool isClaudeDeployed;
    [ObservableProperty] private bool isCodexDeployed;
    [ObservableProperty] private bool isCopilotDeployed;
    [ObservableProperty] private bool isGeminiDeployed;

    public bool IsDeployed => IsClaudeDeployed || IsCodexDeployed || IsCopilotDeployed || IsGeminiDeployed;
    public string IsDeployedLabel => IsDeployed ? "ON" : "--";

    public DeployAgentItemViewModel(AgentDefinition definition)
    {
        Definition = definition;
    }

    public void SetDeployedState(CliTarget cli, bool deployed)
    {
        _suppressCallback = true;
        switch (cli)
        {
            case CliTarget.Claude: IsClaudeDeployed = deployed; break;
            case CliTarget.Codex: IsCodexDeployed = deployed; break;
            case CliTarget.Copilot: IsCopilotDeployed = deployed; break;
            case CliTarget.Gemini: IsGeminiDeployed = deployed; break;
        }
        _suppressCallback = false;
        OnPropertyChanged(nameof(IsDeployed));
        OnPropertyChanged(nameof(IsDeployedLabel));
    }

    partial void OnIsClaudeDeployedChanged(bool value)
    {
        if (!_suppressCallback) OnCliToggled?.Invoke(this, CliTarget.Claude, value);
        OnPropertyChanged(nameof(IsDeployed));
        OnPropertyChanged(nameof(IsDeployedLabel));
    }

    partial void OnIsCodexDeployedChanged(bool value)
    {
        if (!_suppressCallback) OnCliToggled?.Invoke(this, CliTarget.Codex, value);
        OnPropertyChanged(nameof(IsDeployed));
        OnPropertyChanged(nameof(IsDeployedLabel));
    }

    partial void OnIsCopilotDeployedChanged(bool value)
    {
        if (!_suppressCallback) OnCliToggled?.Invoke(this, CliTarget.Copilot, value);
        OnPropertyChanged(nameof(IsDeployed));
        OnPropertyChanged(nameof(IsDeployedLabel));
    }

    partial void OnIsGeminiDeployedChanged(bool value)
    {
        if (!_suppressCallback) OnCliToggled?.Invoke(this, CliTarget.Gemini, value);
        OnPropertyChanged(nameof(IsDeployed));
        OnPropertyChanged(nameof(IsDeployedLabel));
    }
}

public partial class DeployRuleItemViewModel : ObservableObject
{
    private bool _suppressCallback;

    public ContextRuleDefinition Definition { get; }
    public string Name => Definition.Name;

    public Action<DeployRuleItemViewModel, CliTarget, bool>? OnCliToggled;

    [ObservableProperty] private bool isClaudeDeployed;
    [ObservableProperty] private bool isCodexDeployed;
    [ObservableProperty] private bool isCopilotDeployed;
    [ObservableProperty] private bool isGeminiDeployed;

    public bool IsDeployed => IsClaudeDeployed || IsCodexDeployed || IsCopilotDeployed || IsGeminiDeployed;
    public string IsDeployedLabel => IsDeployed ? "ON" : "--";

    public DeployRuleItemViewModel(ContextRuleDefinition definition)
    {
        Definition = definition;
    }

    public void SetDeployedState(CliTarget cli, bool deployed)
    {
        _suppressCallback = true;
        switch (cli)
        {
            case CliTarget.Claude: IsClaudeDeployed = deployed; break;
            case CliTarget.Codex: IsCodexDeployed = deployed; break;
            case CliTarget.Copilot: IsCopilotDeployed = deployed; break;
            case CliTarget.Gemini: IsGeminiDeployed = deployed; break;
        }
        _suppressCallback = false;
        OnPropertyChanged(nameof(IsDeployed));
        OnPropertyChanged(nameof(IsDeployedLabel));
    }

    partial void OnIsClaudeDeployedChanged(bool value)
    {
        if (!_suppressCallback) OnCliToggled?.Invoke(this, CliTarget.Claude, value);
        OnPropertyChanged(nameof(IsDeployed));
        OnPropertyChanged(nameof(IsDeployedLabel));
    }

    partial void OnIsCodexDeployedChanged(bool value)
    {
        if (!_suppressCallback) OnCliToggled?.Invoke(this, CliTarget.Codex, value);
        OnPropertyChanged(nameof(IsDeployed));
        OnPropertyChanged(nameof(IsDeployedLabel));
    }

    partial void OnIsCopilotDeployedChanged(bool value)
    {
        if (!_suppressCallback) OnCliToggled?.Invoke(this, CliTarget.Copilot, value);
        OnPropertyChanged(nameof(IsDeployed));
        OnPropertyChanged(nameof(IsDeployedLabel));
    }

    partial void OnIsGeminiDeployedChanged(bool value)
    {
        if (!_suppressCallback) OnCliToggled?.Invoke(this, CliTarget.Gemini, value);
        OnPropertyChanged(nameof(IsDeployed));
        OnPropertyChanged(nameof(IsDeployedLabel));
    }
}

// ─── Main ViewModel ───────────────────────────────────────────────────────────

public partial class AgentHubViewModel : ObservableObject
{
    private readonly AgentHubService _agentHubService;
    private readonly AgentDeploymentService _deploymentService;
    private readonly ConfigService _configService;
    private readonly ProjectDiscoveryService _discoveryService;

    // ── Master Library ────────────────────────────────────────────────────

    [ObservableProperty] private ObservableCollection<AgentDefinition> agentDefinitions = [];
    [ObservableProperty] private ObservableCollection<ContextRuleDefinition> ruleDefinitions = [];

    [ObservableProperty] private AgentDefinition? selectedAgentDefinition;
    [ObservableProperty] private ContextRuleDefinition? selectedRuleDefinition;
    [ObservableProperty] private string selectedPreviewContent = "";

    public bool IsAgentSelected => SelectedAgentDefinition != null;
    public bool IsRuleSelected => SelectedRuleDefinition != null;
    public bool IsAnyDefinitionSelected => IsAgentSelected || IsRuleSelected;

    // ── Deployment Panel ──────────────────────────────────────────────────

    [ObservableProperty] private ObservableCollection<ProjectInfo> projects = [];
    [ObservableProperty] private ProjectInfo? selectedProject;
    [ObservableProperty] private string targetSubPath = "";
    [ObservableProperty] private ObservableCollection<string> targetSubPathCandidates = [];

    [ObservableProperty] private ObservableCollection<DeployAgentItemViewModel> agentDeployItems = [];
    [ObservableProperty] private ObservableCollection<DeployRuleItemViewModel> ruleDeployItems = [];

    [ObservableProperty] private bool isAiEnabled;
    [ObservableProperty] private bool isRefreshing;
    [ObservableProperty] private string statusMessage = "";

    public AgentHubViewModel(
        AgentHubService agentHubService,
        AgentDeploymentService deploymentService,
        ConfigService configService,
        ProjectDiscoveryService discoveryService)
    {
        _agentHubService = agentHubService;
        _deploymentService = deploymentService;
        _configService = configService;
        _discoveryService = discoveryService;

        IsAiEnabled = configService.LoadSettings().AiEnabled;
        WeakReferenceMessenger.Default.Register<AiEnabledChangedMessage>(this,
            (_, msg) => IsAiEnabled = msg.Enabled);
    }

    // ── Property Change Handlers ──────────────────────────────────────────

    partial void OnSelectedAgentDefinitionChanged(AgentDefinition? value)
    {
        if (value != null)
        {
            SelectedRuleDefinition = null;
            SelectedPreviewContent = _agentHubService.GetAgentContent(value);
        }
        else if (SelectedRuleDefinition == null)
        {
            SelectedPreviewContent = "";
        }

        OnPropertyChanged(nameof(IsAgentSelected));
        OnPropertyChanged(nameof(IsRuleSelected));
        OnPropertyChanged(nameof(IsAnyDefinitionSelected));
    }

    partial void OnSelectedRuleDefinitionChanged(ContextRuleDefinition? value)
    {
        if (value != null)
        {
            SelectedAgentDefinition = null;
            SelectedPreviewContent = _agentHubService.GetRuleContent(value);
        }
        else if (SelectedAgentDefinition == null)
        {
            SelectedPreviewContent = "";
        }

        OnPropertyChanged(nameof(IsAgentSelected));
        OnPropertyChanged(nameof(IsRuleSelected));
        OnPropertyChanged(nameof(IsAnyDefinitionSelected));
    }

    partial void OnSelectedProjectChanged(ProjectInfo? value)
    {
        LoadTargetSubPathCandidates();
        LoadDeploymentState();
    }

    partial void OnTargetSubPathChanged(string value)
    {
        LoadDeploymentState();
    }

    // ── Library Loading ───────────────────────────────────────────────────

    public void LoadLibrary()
    {
        AgentDefinitions.Clear();
        foreach (var def in _agentHubService.GetAgentDefinitions())
            AgentDefinitions.Add(def);

        RuleDefinitions.Clear();
        foreach (var def in _agentHubService.GetRuleDefinitions())
            RuleDefinitions.Add(def);
    }

    public async Task LoadProjectsAsync()
    {
        try
        {
            var list = await _discoveryService.GetProjectInfoListAsync(force: false);
            Projects.Clear();
            foreach (var p in list.OrderBy(p => p.Name))
                Projects.Add(p);

            if (SelectedProject == null && Projects.Count > 0)
                SelectedProject = Projects[0];
            else
                LoadTargetSubPathCandidates();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AgentHubVM] LoadProjects failed: {ex.Message}");
        }
    }

    public void LoadTargetSubPathCandidates()
    {
        TargetSubPathCandidates.Clear();
        TargetSubPathCandidates.Add("");

        if (SelectedProject == null || !Directory.Exists(SelectedProject.Path))
            return;

        var projectRoot = SelectedProject.Path;
        var commonRoots = new[]
        {
            "development",
            Path.Combine("development", "source"),
            "shared"
        };

        foreach (var relative in commonRoots)
        {
            var full = Path.Combine(projectRoot, relative);
            if (Directory.Exists(full))
                TargetSubPathCandidates.Add(relative.Replace('/', '\\'));
        }

        var scanRoots = new[]
        {
            projectRoot,
            Path.Combine(projectRoot, "development"),
            Path.Combine(projectRoot, "development", "source")
        };

        foreach (var root in scanRoots.Where(Directory.Exists))
        {
            foreach (var dir in Directory.GetDirectories(root))
            {
                var name = Path.GetFileName(dir);
                if (name.StartsWith(".", StringComparison.Ordinal))
                    continue;

                var relative = Path.GetRelativePath(projectRoot, dir).Replace('/', '\\');
                if (!TargetSubPathCandidates.Contains(relative, StringComparer.OrdinalIgnoreCase))
                    TargetSubPathCandidates.Add(relative);
            }
        }
    }

    // ── Deployment State ──────────────────────────────────────────────────

    public void LoadDeploymentState()
    {
        AgentDeployItems.Clear();
        RuleDeployItems.Clear();

        if (SelectedProject == null) return;

        foreach (var def in AgentDefinitions)
        {
            var item = new DeployAgentItemViewModel(def);
            foreach (var cli in Enum.GetValues<CliTarget>())
                item.SetDeployedState(cli, _deploymentService.IsAgentDeployed(SelectedProject, TargetSubPath, def, cli));
            item.OnCliToggled = OnAgentCliToggled;
            AgentDeployItems.Add(item);
        }

        foreach (var def in RuleDefinitions)
        {
            var item = new DeployRuleItemViewModel(def);
            foreach (var cli in Enum.GetValues<CliTarget>())
                item.SetDeployedState(cli, _deploymentService.IsRuleDeployed(SelectedProject, TargetSubPath, def, cli));
            item.OnCliToggled = OnRuleCliToggled;
            RuleDeployItems.Add(item);
        }
    }

    private void OnAgentCliToggled(DeployAgentItemViewModel item, CliTarget cli, bool enabled)
    {
        if (SelectedProject == null) return;
        try
        {
            if (enabled)
            {
                var content = _agentHubService.GetAgentContent(item.Definition);
                _deploymentService.DeployAgent(SelectedProject, TargetSubPath, item.Definition, content, cli);
                StatusMessage = $"Deployed '{item.Name}' to {cli}";
            }
            else
            {
                _deploymentService.UndeployAgent(SelectedProject, TargetSubPath, item.Definition, cli);
                StatusMessage = $"Undeployed '{item.Name}' from {cli}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            item.SetDeployedState(cli, !enabled);
        }
    }

    private void OnRuleCliToggled(DeployRuleItemViewModel item, CliTarget cli, bool enabled)
    {
        if (SelectedProject == null) return;
        try
        {
            if (enabled)
            {
                var content = _agentHubService.GetRuleContent(item.Definition);
                _deploymentService.DeployRule(SelectedProject, TargetSubPath, item.Definition, content, cli);
                StatusMessage = $"Deployed rule '{item.Name}' to {cli}";
            }
            else
            {
                _deploymentService.UndeployRule(SelectedProject, TargetSubPath, item.Definition, cli);
                StatusMessage = $"Undeployed rule '{item.Name}' from {cli}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            item.SetDeployedState(cli, !enabled);
        }
    }

    // ── Commands ──────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task SyncStatus()
    {
        if (SelectedProject == null) return;
        IsRefreshing = true;
        StatusMessage = "Syncing...";
        try
        {
            await Task.Run(() => _deploymentService.SyncDeploymentState(
                [.. AgentDefinitions],
                [.. RuleDefinitions],
                [.. Projects]));
            LoadDeploymentState();
            StatusMessage = "Sync complete";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Sync error: {ex.Message}";
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    [RelayCommand]
    private async Task SyncAllProjects()
    {
        IsRefreshing = true;
        StatusMessage = "Syncing all projects...";
        try
        {
            await Task.Run(() => _deploymentService.SyncDeploymentState(
                [.. AgentDefinitions],
                [.. RuleDefinitions],
                [.. Projects]));
            LoadDeploymentState();
            StatusMessage = "All project sync complete";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Sync error: {ex.Message}";
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    [RelayCommand]
    private async Task DeploySelectionToAllProjects()
    {
        if (Projects.Count == 0)
            return;

        IsRefreshing = true;
        StatusMessage = "Deploying selection to all projects...";
        try
        {
            await Task.Run(() =>
            {
                foreach (var project in Projects)
                {
                    foreach (var item in AgentDeployItems)
                    {
                        DeployOrUndeployAgent(project, item, CliTarget.Claude, item.IsClaudeDeployed);
                        DeployOrUndeployAgent(project, item, CliTarget.Codex, item.IsCodexDeployed);
                        DeployOrUndeployAgent(project, item, CliTarget.Copilot, item.IsCopilotDeployed);
                        DeployOrUndeployAgent(project, item, CliTarget.Gemini, item.IsGeminiDeployed);
                    }

                    foreach (var item in RuleDeployItems)
                    {
                        DeployOrUndeployRule(project, item, CliTarget.Claude, item.IsClaudeDeployed);
                        DeployOrUndeployRule(project, item, CliTarget.Codex, item.IsCodexDeployed);
                        DeployOrUndeployRule(project, item, CliTarget.Copilot, item.IsCopilotDeployed);
                        DeployOrUndeployRule(project, item, CliTarget.Gemini, item.IsGeminiDeployed);
                    }
                }
            });

            LoadDeploymentState();
            StatusMessage = "Batch deployment complete";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Batch deploy error: {ex.Message}";
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    [RelayCommand]
    private async Task RefreshProjects()
    {
        IsRefreshing = true;
        try
        {
            await LoadProjectsAsync();
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    public void ExportLibraryZip(string outputZipPath)
    {
        _agentHubService.ExportLibraryZip(outputZipPath);
    }

    public void ImportLibraryZip(string sourceZipPath)
    {
        _agentHubService.ImportLibraryZip(sourceZipPath);
        LoadLibrary();
        LoadDeploymentState();
    }

    public void ImportAgentMarkdown(string sourceMarkdownPath)
    {
        _agentHubService.ImportAgentFromMarkdown(sourceMarkdownPath);
        LoadLibrary();
        LoadDeploymentState();
    }

    public void ImportRuleMarkdown(string sourceMarkdownPath)
    {
        _agentHubService.ImportRuleFromMarkdown(sourceMarkdownPath);
        LoadLibrary();
        LoadDeploymentState();
    }

    // ── Agent CRUD ────────────────────────────────────────────────────────

    public void SaveAgent(
        string? existingId,
        string name,
        string description,
        string content,
        string frontmatterClaude,
        string frontmatterCodex,
        string frontmatterCopilot,
        string frontmatterGemini)
    {
        var def = string.IsNullOrEmpty(existingId)
            ? new AgentDefinition { Name = name, Description = description }
            : AgentDefinitions.FirstOrDefault(a => a.Id == existingId)
              ?? new AgentDefinition { Id = existingId, Name = name };

        def.Name = name;
        def.Description = description;
        def.FrontmatterClaude = frontmatterClaude;
        def.FrontmatterCodex = frontmatterCodex;
        def.FrontmatterCopilot = frontmatterCopilot;
        def.FrontmatterGemini = frontmatterGemini;
        _agentHubService.SaveAgentDefinition(def, content);

        LoadLibrary();
        SelectedAgentDefinition = AgentDefinitions.FirstOrDefault(a => a.Id == def.Id);
        LoadDeploymentState();
    }

    public void DeleteAgent(string agentId)
    {
        _agentHubService.DeleteAgentDefinition(agentId);
        LoadLibrary();
        SelectedAgentDefinition = null;
        SelectedPreviewContent = "";
        LoadDeploymentState();
    }

    // ── Rule CRUD ─────────────────────────────────────────────────────────

    public void SaveRule(string? existingId, string name, string description, string content)
    {
        var def = string.IsNullOrEmpty(existingId)
            ? new ContextRuleDefinition { Name = name, Description = description }
            : RuleDefinitions.FirstOrDefault(r => r.Id == existingId)
              ?? new ContextRuleDefinition { Id = existingId, Name = name };

        def.Name = name;
        def.Description = description;
        _agentHubService.SaveRuleDefinition(def, content);

        LoadLibrary();
        SelectedRuleDefinition = RuleDefinitions.FirstOrDefault(r => r.Id == def.Id);
        LoadDeploymentState();
    }

    public void DeleteRule(string ruleId)
    {
        _agentHubService.DeleteRuleDefinition(ruleId);
        LoadLibrary();
        SelectedRuleDefinition = null;
        SelectedPreviewContent = "";
        LoadDeploymentState();
    }

    private void DeployOrUndeployAgent(ProjectInfo project, DeployAgentItemViewModel item, CliTarget cli, bool enabled)
    {
        if (enabled)
        {
            var content = _agentHubService.GetAgentContent(item.Definition);
            _deploymentService.DeployAgent(project, TargetSubPath, item.Definition, content, cli);
        }
        else
        {
            _deploymentService.UndeployAgent(project, TargetSubPath, item.Definition, cli);
        }
    }

    private void DeployOrUndeployRule(ProjectInfo project, DeployRuleItemViewModel item, CliTarget cli, bool enabled)
    {
        if (enabled)
        {
            var content = _agentHubService.GetRuleContent(item.Definition);
            _deploymentService.DeployRule(project, TargetSubPath, item.Definition, content, cli);
        }
        else
        {
            _deploymentService.UndeployRule(project, TargetSubPath, item.Definition, cli);
        }
    }
}
