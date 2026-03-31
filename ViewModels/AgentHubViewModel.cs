using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using ProjectCurator.Models;
using ProjectCurator.Services;

namespace ProjectCurator.ViewModels;

// ─── Tab Enum ────────────────────────────────────────────────────────────────

public enum LibraryTab { Agents, Rules, Skills }

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

    public bool? IsAllDeployed
    {
        get => IsClaudeDeployed && IsCodexDeployed && IsCopilotDeployed && IsGeminiDeployed ? true
             : !IsClaudeDeployed && !IsCodexDeployed && !IsCopilotDeployed && !IsGeminiDeployed ? false
             : (bool?)null;
        set
        {
            var deploy = value == true;
            IsClaudeDeployed = deploy;
            IsCodexDeployed = deploy;
            IsCopilotDeployed = deploy;
            IsGeminiDeployed = deploy;
            OnPropertyChanged();
        }
    }

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
        OnPropertyChanged(nameof(IsAllDeployed));
    }

    partial void OnIsClaudeDeployedChanged(bool value)
    {
        if (!_suppressCallback) OnCliToggled?.Invoke(this, CliTarget.Claude, value);
        OnPropertyChanged(nameof(IsDeployed));
        OnPropertyChanged(nameof(IsDeployedLabel));
        OnPropertyChanged(nameof(IsAllDeployed));
    }

    partial void OnIsCodexDeployedChanged(bool value)
    {
        if (!_suppressCallback) OnCliToggled?.Invoke(this, CliTarget.Codex, value);
        OnPropertyChanged(nameof(IsDeployed));
        OnPropertyChanged(nameof(IsDeployedLabel));
        OnPropertyChanged(nameof(IsAllDeployed));
    }

    partial void OnIsCopilotDeployedChanged(bool value)
    {
        if (!_suppressCallback) OnCliToggled?.Invoke(this, CliTarget.Copilot, value);
        OnPropertyChanged(nameof(IsDeployed));
        OnPropertyChanged(nameof(IsDeployedLabel));
        OnPropertyChanged(nameof(IsAllDeployed));
    }

    partial void OnIsGeminiDeployedChanged(bool value)
    {
        if (!_suppressCallback) OnCliToggled?.Invoke(this, CliTarget.Gemini, value);
        OnPropertyChanged(nameof(IsDeployed));
        OnPropertyChanged(nameof(IsDeployedLabel));
        OnPropertyChanged(nameof(IsAllDeployed));
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

    public bool? IsAllDeployed
    {
        get => IsClaudeDeployed && IsCodexDeployed && IsCopilotDeployed && IsGeminiDeployed ? true
             : !IsClaudeDeployed && !IsCodexDeployed && !IsCopilotDeployed && !IsGeminiDeployed ? false
             : (bool?)null;
        set
        {
            var deploy = value == true;
            IsClaudeDeployed = deploy;
            IsCodexDeployed = deploy;
            IsCopilotDeployed = deploy;
            IsGeminiDeployed = deploy;
            OnPropertyChanged();
        }
    }

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
        OnPropertyChanged(nameof(IsAllDeployed));
    }

    partial void OnIsClaudeDeployedChanged(bool value)
    {
        if (!_suppressCallback) OnCliToggled?.Invoke(this, CliTarget.Claude, value);
        OnPropertyChanged(nameof(IsDeployed));
        OnPropertyChanged(nameof(IsDeployedLabel));
        OnPropertyChanged(nameof(IsAllDeployed));
    }

    partial void OnIsCodexDeployedChanged(bool value)
    {
        if (!_suppressCallback) OnCliToggled?.Invoke(this, CliTarget.Codex, value);
        OnPropertyChanged(nameof(IsDeployed));
        OnPropertyChanged(nameof(IsDeployedLabel));
        OnPropertyChanged(nameof(IsAllDeployed));
    }

    partial void OnIsCopilotDeployedChanged(bool value)
    {
        if (!_suppressCallback) OnCliToggled?.Invoke(this, CliTarget.Copilot, value);
        OnPropertyChanged(nameof(IsDeployed));
        OnPropertyChanged(nameof(IsDeployedLabel));
        OnPropertyChanged(nameof(IsAllDeployed));
    }

    partial void OnIsGeminiDeployedChanged(bool value)
    {
        if (!_suppressCallback) OnCliToggled?.Invoke(this, CliTarget.Gemini, value);
        OnPropertyChanged(nameof(IsDeployed));
        OnPropertyChanged(nameof(IsDeployedLabel));
        OnPropertyChanged(nameof(IsAllDeployed));
    }
}

public partial class DeploySkillItemViewModel : ObservableObject
{
    private bool _suppressCallback;

    public SkillDefinition Definition { get; }
    public string Name => Definition.Name;
    public bool IsBuiltIn => Definition.IsBuiltIn;

    public Action<DeploySkillItemViewModel, CliTarget, bool>? OnCliToggled;

    [ObservableProperty] private bool isClaudeDeployed;
    [ObservableProperty] private bool isCodexDeployed;
    [ObservableProperty] private bool isCopilotDeployed;
    [ObservableProperty] private bool isGeminiDeployed;

    public bool IsDeployed => IsClaudeDeployed || IsCodexDeployed || IsCopilotDeployed || IsGeminiDeployed;
    public string IsDeployedLabel => IsDeployed ? "ON" : "--";

    public bool? IsAllDeployed
    {
        get => IsClaudeDeployed && IsCodexDeployed && IsCopilotDeployed && IsGeminiDeployed ? true
             : !IsClaudeDeployed && !IsCodexDeployed && !IsCopilotDeployed && !IsGeminiDeployed ? false
             : (bool?)null;
        set
        {
            var deploy = value == true;
            IsClaudeDeployed = deploy;
            IsCodexDeployed = deploy;
            IsCopilotDeployed = deploy;
            IsGeminiDeployed = deploy;
            OnPropertyChanged();
        }
    }

    public DeploySkillItemViewModel(SkillDefinition definition)
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
        OnPropertyChanged(nameof(IsAllDeployed));
    }

    partial void OnIsClaudeDeployedChanged(bool value)
    {
        if (!_suppressCallback) OnCliToggled?.Invoke(this, CliTarget.Claude, value);
        OnPropertyChanged(nameof(IsDeployed));
        OnPropertyChanged(nameof(IsDeployedLabel));
        OnPropertyChanged(nameof(IsAllDeployed));
    }

    partial void OnIsCodexDeployedChanged(bool value)
    {
        if (!_suppressCallback) OnCliToggled?.Invoke(this, CliTarget.Codex, value);
        OnPropertyChanged(nameof(IsDeployed));
        OnPropertyChanged(nameof(IsDeployedLabel));
        OnPropertyChanged(nameof(IsAllDeployed));
    }

    partial void OnIsCopilotDeployedChanged(bool value)
    {
        if (!_suppressCallback) OnCliToggled?.Invoke(this, CliTarget.Copilot, value);
        OnPropertyChanged(nameof(IsDeployed));
        OnPropertyChanged(nameof(IsDeployedLabel));
        OnPropertyChanged(nameof(IsAllDeployed));
    }

    partial void OnIsGeminiDeployedChanged(bool value)
    {
        if (!_suppressCallback) OnCliToggled?.Invoke(this, CliTarget.Gemini, value);
        OnPropertyChanged(nameof(IsDeployed));
        OnPropertyChanged(nameof(IsDeployedLabel));
        OnPropertyChanged(nameof(IsAllDeployed));
    }
}

// ─── Main ViewModel ───────────────────────────────────────────────────────────

public partial class AgentHubViewModel : ObservableObject
{
    private readonly AgentHubService _agentHubService;
    private readonly AgentDeploymentService _deploymentService;
    private readonly ConfigService _configService;
    private readonly ProjectDiscoveryService _discoveryService;
    private CancellationTokenSource? _deploymentRefreshCts;
    private int _deploymentRefreshVersion;
    private CancellationTokenSource? _previewLoadCts;
    private int _previewLoadVersion;

    // ── Master Library ────────────────────────────────────────────────────

    [ObservableProperty] private ObservableCollection<AgentDefinition> agentDefinitions = [];
    [ObservableProperty] private ObservableCollection<ContextRuleDefinition> ruleDefinitions = [];
    [ObservableProperty] private ObservableCollection<SkillDefinition> skillDefinitions = [];

    [ObservableProperty] private AgentDefinition? selectedAgentDefinition;
    [ObservableProperty] private ContextRuleDefinition? selectedRuleDefinition;
    [ObservableProperty] private SkillDefinition? selectedSkillDefinition;
    [ObservableProperty] private string selectedPreviewContent = "";

    [ObservableProperty] private LibraryTab selectedLibraryTab = LibraryTab.Agents;

    public bool IsAgentSelected => SelectedAgentDefinition != null;
    public bool IsRuleSelected => SelectedRuleDefinition != null;
    public bool IsSkillSelected => SelectedSkillDefinition != null;
    public bool IsAnyDefinitionSelected => IsAgentSelected || IsRuleSelected || IsSkillSelected;
    public bool CanEditSelected => IsAnyDefinitionSelected;
    public bool CanDeleteSelected =>
        IsAgentSelected || IsRuleSelected ||
        (IsSkillSelected && !(SelectedSkillDefinition?.IsBuiltIn ?? false));

    public bool IsAgentsTabSelected => SelectedLibraryTab == LibraryTab.Agents;
    public bool IsRulesTabSelected => SelectedLibraryTab == LibraryTab.Rules;
    public bool IsSkillsTabSelected => SelectedLibraryTab == LibraryTab.Skills;

    // ── Deployment Panel ──────────────────────────────────────────────────

    [ObservableProperty] private ObservableCollection<ProjectInfo> projects = [];
    [ObservableProperty] private ProjectInfo? selectedProject;
    [ObservableProperty] private string targetSubPath = "";
    [ObservableProperty] private ObservableCollection<string> targetSubPathCandidates = [];

    [ObservableProperty] private ObservableCollection<DeployAgentItemViewModel> agentDeployItems = [];
    [ObservableProperty] private ObservableCollection<DeployRuleItemViewModel> ruleDeployItems = [];
    [ObservableProperty] private ObservableCollection<DeploySkillItemViewModel> skillDeployItems = [];

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
            SelectedSkillDefinition = null;
            QueuePreviewLoad(() => _agentHubService.GetAgentContent(value));
        }
        else if (SelectedRuleDefinition == null && SelectedSkillDefinition == null)
        {
            SelectedPreviewContent = "";
        }

        OnPropertyChanged(nameof(IsAgentSelected));
        OnPropertyChanged(nameof(IsRuleSelected));
        OnPropertyChanged(nameof(IsSkillSelected));
        OnPropertyChanged(nameof(IsAnyDefinitionSelected));
        OnPropertyChanged(nameof(CanEditSelected));
        OnPropertyChanged(nameof(CanDeleteSelected));
    }

    partial void OnSelectedRuleDefinitionChanged(ContextRuleDefinition? value)
    {
        if (value != null)
        {
            SelectedAgentDefinition = null;
            SelectedSkillDefinition = null;
            QueuePreviewLoad(() => _agentHubService.GetRuleContent(value));
        }
        else if (SelectedAgentDefinition == null && SelectedSkillDefinition == null)
        {
            SelectedPreviewContent = "";
        }

        OnPropertyChanged(nameof(IsAgentSelected));
        OnPropertyChanged(nameof(IsRuleSelected));
        OnPropertyChanged(nameof(IsSkillSelected));
        OnPropertyChanged(nameof(IsAnyDefinitionSelected));
        OnPropertyChanged(nameof(CanEditSelected));
        OnPropertyChanged(nameof(CanDeleteSelected));
    }

    partial void OnSelectedSkillDefinitionChanged(SkillDefinition? value)
    {
        if (value != null)
        {
            SelectedAgentDefinition = null;
            SelectedRuleDefinition = null;
            QueuePreviewLoad(() => _agentHubService.GetSkillContent(value));
        }
        else if (SelectedAgentDefinition == null && SelectedRuleDefinition == null)
        {
            SelectedPreviewContent = "";
        }

        OnPropertyChanged(nameof(IsAgentSelected));
        OnPropertyChanged(nameof(IsRuleSelected));
        OnPropertyChanged(nameof(IsSkillSelected));
        OnPropertyChanged(nameof(IsAnyDefinitionSelected));
        OnPropertyChanged(nameof(CanEditSelected));
        OnPropertyChanged(nameof(CanDeleteSelected));
    }

    partial void OnSelectedLibraryTabChanged(LibraryTab value)
    {
        OnPropertyChanged(nameof(IsAgentsTabSelected));
        OnPropertyChanged(nameof(IsRulesTabSelected));
        OnPropertyChanged(nameof(IsSkillsTabSelected));
    }

    partial void OnSelectedProjectChanged(ProjectInfo? value)
    {
        QueueDeploymentRefresh(includeTargetCandidates: true);
    }

    partial void OnTargetSubPathChanged(string value)
    {
        QueueDeploymentRefresh(includeTargetCandidates: false);
    }

    // ── Library Loading ───────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        IsRefreshing = true;
        StatusMessage = "Loading Agent Hub...";
        try
        {
            await LoadLibraryAsync();
            await LoadProjectsAsync();
            StatusMessage = "";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Init error: {ex.Message}";
            Debug.WriteLine($"[AgentHubVM] Initialize failed: {ex}");
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    public async Task LoadLibraryAsync()
    {
        var snapshot = await Task.Run(() =>
        {
            _agentHubService.EnsureBuiltInSkills();
            return new LibrarySnapshot(
                _agentHubService.GetAgentDefinitions(),
                _agentHubService.GetRuleDefinitions(),
                _agentHubService.GetSkillDefinitions());
        });

        AgentDefinitions = new ObservableCollection<AgentDefinition>(snapshot.Agents);
        RuleDefinitions = new ObservableCollection<ContextRuleDefinition>(snapshot.Rules);
        SkillDefinitions = new ObservableCollection<SkillDefinition>(snapshot.Skills);
    }

    public void LoadLibrary()
    {
        _agentHubService.EnsureBuiltInSkills();

        AgentDefinitions.Clear();
        foreach (var def in _agentHubService.GetAgentDefinitions())
            AgentDefinitions.Add(def);

        RuleDefinitions.Clear();
        foreach (var def in _agentHubService.GetRuleDefinitions())
            RuleDefinitions.Add(def);

        SkillDefinitions.Clear();
        foreach (var def in _agentHubService.GetSkillDefinitions())
            SkillDefinitions.Add(def);
    }

    public async Task LoadProjectsAsync()
    {
        try
        {
            var list = await _discoveryService.GetProjectInfoListAsync(force: false);
            var hiddenKeys = _configService.LoadHiddenProjects();
            var currentHiddenKey = SelectedProject?.HiddenKey;
            var visibleProjects = list
                .Where(p => !hiddenKeys.Contains(p.HiddenKey))
                .ToList();

            Projects = new ObservableCollection<ProjectInfo>(visibleProjects);

            if (Projects.Count == 0)
            {
                SelectedProject = null;
                TargetSubPathCandidates = new ObservableCollection<string>([""]);
                ClearDeploymentItems();
                return;
            }

            SelectedProject =
                Projects.FirstOrDefault(p => p.HiddenKey == currentHiddenKey)
                ?? Projects[0];
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AgentHubVM] LoadProjects failed: {ex.Message}");
        }
    }

    public void LoadTargetSubPathCandidates()
    {
        TargetSubPathCandidates = new ObservableCollection<string>(
            BuildTargetSubPathCandidates(SelectedProject));
    }

    // ── Deployment State ──────────────────────────────────────────────────

    public void LoadDeploymentState()
    {
        var project = SelectedProject;
        if (project == null)
        {
            ClearDeploymentItems();
            return;
        }

        var snapshot = BuildDeploymentSnapshot(
            project,
            TargetSubPath,
            [.. AgentDefinitions],
            [.. RuleDefinitions],
            [.. SkillDefinitions]);

        ApplyDeploymentSnapshot(snapshot);
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

    private void OnSkillCliToggled(DeploySkillItemViewModel item, CliTarget cli, bool enabled)
    {
        if (SelectedProject == null) return;
        try
        {
            if (enabled)
            {
                _deploymentService.DeploySkill(SelectedProject, TargetSubPath, item.Definition, cli);
                StatusMessage = $"Deployed skill '{item.Name}' to {cli}";
            }
            else
            {
                _deploymentService.UndeploySkill(SelectedProject, TargetSubPath, item.Definition, cli);
                StatusMessage = $"Undeployed skill '{item.Name}' from {cli}";
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
                [.. Projects],
                [.. SkillDefinitions]));
            QueueDeploymentRefresh(includeTargetCandidates: false);
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
                [.. Projects],
                [.. SkillDefinitions]));
            QueueDeploymentRefresh(includeTargetCandidates: false);
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

                    foreach (var item in SkillDeployItems)
                    {
                        DeployOrUndeploySkill(project, item, CliTarget.Claude, item.IsClaudeDeployed);
                        DeployOrUndeploySkill(project, item, CliTarget.Codex, item.IsCodexDeployed);
                        DeployOrUndeploySkill(project, item, CliTarget.Copilot, item.IsCopilotDeployed);
                        DeployOrUndeploySkill(project, item, CliTarget.Gemini, item.IsGeminiDeployed);
                    }
                }
            });

            QueueDeploymentRefresh(includeTargetCandidates: false);
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
        QueueDeploymentRefresh(includeTargetCandidates: true);
    }

    // ── Agent CRUD ────────────────────────────────────────────────────────

    public (string Body, string ExtraFrontmatter) GetAgentContentForEdit(AgentDefinition def)
        => _agentHubService.GetAgentContentForEdit(def);

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
        QueueDeploymentRefresh(includeTargetCandidates: false);
    }

    public void DeleteAgent(string agentId)
    {
        _agentHubService.DeleteAgentDefinition(agentId);
        LoadLibrary();
        SelectedAgentDefinition = null;
        SelectedPreviewContent = "";
        QueueDeploymentRefresh(includeTargetCandidates: false);
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
        QueueDeploymentRefresh(includeTargetCandidates: false);
    }

    public void DeleteRule(string ruleId)
    {
        _agentHubService.DeleteRuleDefinition(ruleId);
        LoadLibrary();
        SelectedRuleDefinition = null;
        SelectedPreviewContent = "";
        QueueDeploymentRefresh(includeTargetCandidates: false);
    }

    // ── Skill CRUD ────────────────────────────────────────────────────────

    public void SaveSkill(string? existingId, string name, string description, string content)
    {
        var def = string.IsNullOrEmpty(existingId)
            ? new SkillDefinition { Name = name, Description = description }
            : SkillDefinitions.FirstOrDefault(s => s.Id == existingId)
              ?? new SkillDefinition { Id = existingId, Name = name };

        def.Name = name;
        def.Description = description;
        _agentHubService.SaveSkillDefinition(def, content);

        LoadLibrary();
        SelectedSkillDefinition = SkillDefinitions.FirstOrDefault(s => s.Id == def.Id);
        QueueDeploymentRefresh(includeTargetCandidates: false);
    }

    public void DeleteSkill(string skillId)
    {
        _agentHubService.DeleteSkillDefinition(skillId);
        LoadLibrary();
        SelectedSkillDefinition = null;
        SelectedPreviewContent = "";
        QueueDeploymentRefresh(includeTargetCandidates: false);
    }

    public string GetSkillContentForEdit(SkillDefinition def)
        => _agentHubService.GetSkillContent(def);

    public string GetSkillFolderPath(SkillDefinition def)
        => def.ContentDirectory;

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

    private void DeployOrUndeploySkill(ProjectInfo project, DeploySkillItemViewModel item, CliTarget cli, bool enabled)
    {
        if (enabled)
            _deploymentService.DeploySkill(project, TargetSubPath, item.Definition, cli);
        else
            _deploymentService.UndeploySkill(project, TargetSubPath, item.Definition, cli);
    }

    private sealed record LibrarySnapshot(
        List<AgentDefinition> Agents,
        List<ContextRuleDefinition> Rules,
        List<SkillDefinition> Skills);

    private sealed record DeploymentSnapshot(
        List<DeployAgentItemViewModel> Agents,
        List<DeployRuleItemViewModel> Rules,
        List<DeploySkillItemViewModel> Skills);

    private void QueuePreviewLoad(Func<string> loader)
    {
        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _previewLoadCts, cts);
        previous?.Cancel();
        previous?.Dispose();

        var version = Interlocked.Increment(ref _previewLoadVersion);
        SelectedPreviewContent = "Loading preview...";
        _ = LoadPreviewAsync(loader, version, cts.Token);
    }

    private async Task LoadPreviewAsync(Func<string> loader, int version, CancellationToken ct)
    {
        try
        {
            var content = await Task.Run(loader, ct);
            if (ct.IsCancellationRequested || version != _previewLoadVersion)
                return;

            SelectedPreviewContent = content;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (version == _previewLoadVersion)
                SelectedPreviewContent = $"Preview load error: {ex.Message}";
        }
    }

    private void QueueDeploymentRefresh(bool includeTargetCandidates)
    {
        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _deploymentRefreshCts, cts);
        previous?.Cancel();
        previous?.Dispose();

        var version = Interlocked.Increment(ref _deploymentRefreshVersion);
        _ = RefreshDeploymentStateAsync(includeTargetCandidates, version, cts.Token);
    }

    private async Task RefreshDeploymentStateAsync(bool includeTargetCandidates, int version, CancellationToken ct)
    {
        try
        {
            var project = SelectedProject;
            if (project == null)
            {
                if (version != _deploymentRefreshVersion)
                    return;

                TargetSubPathCandidates = new ObservableCollection<string>([""]);
                ClearDeploymentItems();
                return;
            }

            var targetSubPath = TargetSubPath;
            List<AgentDefinition> agentDefs = [.. AgentDefinitions];
            List<ContextRuleDefinition> ruleDefs = [.. RuleDefinitions];
            List<SkillDefinition> skillDefs = [.. SkillDefinitions];

            var result = await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                var candidates = includeTargetCandidates
                    ? BuildTargetSubPathCandidates(project)
                    : null;
                var snapshot = BuildDeploymentSnapshot(project, targetSubPath, agentDefs, ruleDefs, skillDefs);
                return (candidates, snapshot);
            }, ct);

            if (ct.IsCancellationRequested || version != _deploymentRefreshVersion)
                return;

            if (includeTargetCandidates && result.candidates != null)
                TargetSubPathCandidates = new ObservableCollection<string>(result.candidates);

            ApplyDeploymentSnapshot(result.snapshot);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AgentHubVM] Deployment refresh failed: {ex.Message}");
        }
    }

    private static List<string> BuildTargetSubPathCandidates(ProjectInfo? project)
    {
        var candidates = new List<string> { "" };
        if (project == null || !Directory.Exists(project.Path))
            return candidates;

        var projectRoot = project.Path;
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
                candidates.Add(relative.Replace('/', '\\'));
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
                if (!candidates.Contains(relative, StringComparer.OrdinalIgnoreCase))
                    candidates.Add(relative);
            }
        }

        return candidates;
    }

    private DeploymentSnapshot BuildDeploymentSnapshot(
        ProjectInfo selectedProject,
        string targetSubPath,
        List<AgentDefinition> agentDefs,
        List<ContextRuleDefinition> ruleDefs,
        List<SkillDefinition> skillDefs)
    {
        var agentItems = new List<DeployAgentItemViewModel>(agentDefs.Count);
        foreach (var def in agentDefs)
        {
            var item = new DeployAgentItemViewModel(def);
            foreach (var cli in Enum.GetValues<CliTarget>())
                item.SetDeployedState(cli, _deploymentService.IsAgentDeployed(selectedProject, targetSubPath, def, cli));
            item.OnCliToggled = OnAgentCliToggled;
            agentItems.Add(item);
        }

        var ruleItems = new List<DeployRuleItemViewModel>(ruleDefs.Count);
        foreach (var def in ruleDefs)
        {
            var item = new DeployRuleItemViewModel(def);
            foreach (var cli in Enum.GetValues<CliTarget>())
                item.SetDeployedState(cli, _deploymentService.IsRuleDeployed(selectedProject, targetSubPath, def, cli));
            item.OnCliToggled = OnRuleCliToggled;
            ruleItems.Add(item);
        }

        var skillItems = new List<DeploySkillItemViewModel>(skillDefs.Count);
        foreach (var def in skillDefs)
        {
            var item = new DeploySkillItemViewModel(def);
            foreach (var cli in Enum.GetValues<CliTarget>())
                item.SetDeployedState(cli, _deploymentService.IsSkillDeployed(selectedProject, targetSubPath, def, cli));
            item.OnCliToggled = OnSkillCliToggled;
            skillItems.Add(item);
        }

        return new DeploymentSnapshot(agentItems, ruleItems, skillItems);
    }

    private void ApplyDeploymentSnapshot(DeploymentSnapshot snapshot)
    {
        AgentDeployItems = new ObservableCollection<DeployAgentItemViewModel>(snapshot.Agents);
        RuleDeployItems = new ObservableCollection<DeployRuleItemViewModel>(snapshot.Rules);
        SkillDeployItems = new ObservableCollection<DeploySkillItemViewModel>(snapshot.Skills);
    }

    private void ClearDeploymentItems()
    {
        AgentDeployItems = new ObservableCollection<DeployAgentItemViewModel>();
        RuleDeployItems = new ObservableCollection<DeployRuleItemViewModel>();
        SkillDeployItems = new ObservableCollection<DeploySkillItemViewModel>();
    }
}
