using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectCurator.Interfaces;
using ProjectCurator.Models;
using ProjectCurator.Services;

namespace ProjectCurator.ViewModels;

public partial class CommandPaletteViewModel : ObservableObject
{
    private readonly ConfigService _configService;
    private readonly ProjectDiscoveryService _discoveryService;
    private readonly SetupViewModel _setupViewModel;
    private readonly EditorViewModel _editorViewModel;
    private readonly TimelineViewModel _timelineViewModel;
    private readonly StandupGeneratorService _standupGeneratorService;
    private readonly IShellService _shellService;
    private readonly IDialogService _dialogService;

    [ObservableProperty]
    private bool isVisible;

    [ObservableProperty]
    private string searchText = "";

    [ObservableProperty]
    private ObservableCollection<CommandItem> filteredCommands = [];

    [ObservableProperty]
    private CommandItem? selectedCommand;

    private List<CommandItem> _allCommands = [];

    // Callback for showing resume input dialog (platform-specific)
    public Func<string, Task<string?>>? ShowResumeInputDialog;

    // Callback for navigation - accepts page type name (string) for platform independence
    public Action<string>? NavigateToPage;

    public CommandPaletteViewModel(
        ConfigService configService,
        ProjectDiscoveryService discoveryService,
        SetupViewModel setupViewModel,
        EditorViewModel editorViewModel,
        TimelineViewModel timelineViewModel,
        StandupGeneratorService standupGeneratorService,
        IShellService shellService,
        IDialogService dialogService)
    {
        _configService = configService;
        _discoveryService = discoveryService;
        _setupViewModel = setupViewModel;
        _editorViewModel = editorViewModel;
        _timelineViewModel = timelineViewModel;
        _standupGeneratorService = standupGeneratorService;
        _shellService = shellService;
        _dialogService = dialogService;
    }

    public void Show()
    {
        BuildCommands();
        SearchText = "";
        UpdateFilter();
        IsVisible = true;
    }

    public void Hide()
    {
        IsVisible = false;
    }

    partial void OnSearchTextChanged(string value)
    {
        UpdateFilter();
    }

    private void BuildCommands()
    {
        var commands = new List<CommandItem>();

        // --- Tab switch commands ---
        AddTabCommand(commands, "Dashboard", "DashboardPage");
        AddTabCommand(commands, "Editor", "EditorPage");
        AddTabCommand(commands, "Timeline", "TimelinePage");
        AddTabCommand(commands, "Git Repos", "GitReposPage");
        AddTabCommand(commands, "Asana Sync", "AsanaSyncPage");
        AddTabCommand(commands, "Setup", "SetupPage");
        AddTabCommand(commands, "Settings", "SettingsPage");

        // --- Global commands ---
        commands.Add(new CommandItem
        {
            Label = "update focus",
            Category = "project",
            Display = "[>]  update focus (Update Focus from Asana)",
            Action = async (w) =>
            {
                NavigateToPage?.Invoke("EditorPage");
                await Task.Delay(50);
                if (_editorViewModel.UpdateFocusCommand.CanExecute(null))
                    await _editorViewModel.UpdateFocusCommand.ExecuteAsync(null);
            }
        });

        var settings = _configService.LoadSettings();
        if (settings.AiEnabled)
        {
            commands.Add(new CommandItem
            {
                Label = "briefing",
                Category = "project",
                Display = "[>]  briefing (Context Briefing)",
                Action = async (w) =>
                {
                    var selected = _editorViewModel.SelectedProject;
                    if (selected == null)
                    {
                        NavigateToPage?.Invoke("DashboardPage");
                        await _dialogService.ShowMessageAsync(
                            "Briefing",
                            "Select a project in Editor first, then run 'briefing'.");
                    }
                    // TODO: Phase 1 - NavigateToDashboardAndShowBriefingAsync
                }
            });

            commands.Add(new CommandItem
            {
                Label = "meeting",
                Category = "project",
                Display = "[>]  meeting (Import Meeting Notes)",
                Action = async (w) =>
                {
                    NavigateToPage?.Invoke("EditorPage");
                    await Task.Delay(50);
                    if (_editorViewModel.ImportMeetingNotesCommand.CanExecute(null))
                        await _editorViewModel.ImportMeetingNotesCommand.ExecuteAsync(null);
                }
            });
        }

        commands.Add(new CommandItem
        {
            Label = "standup",
            Category = "project",
            Display = "[>]  standup (Daily Standup)",
            Action = async (w) =>
            {
                try
                {
                    await _standupGeneratorService.TryGenerateTodayAsync();
                    var standupPath = _standupGeneratorService.GetTodayStandupPath();
                    if (!string.IsNullOrEmpty(standupPath) && File.Exists(standupPath))
                    {
                        // TODO: Phase 1 - NavigateToEditorAndOpenFile
                        NavigateToPage?.Invoke("EditorPage");
                    }
                }
                catch (Exception ex)
                {
                    await _dialogService.ShowMessageAsync("Standup Error", $"Standup error: {ex.Message}");
                }
            }
        });

        // --- Project commands ---
        var projects = _discoveryService.GetProjectInfoList();
        foreach (var proj in projects)
        {
            string displayName = proj.DisplayName;
            string localName = proj.Name;
            string localPath = proj.Path;

            // > check ProjectName
            commands.Add(new CommandItem
            {
                Label = $"check {localName}",
                Category = "project",
                Display = $"[>]  check {displayName}",
                Action = (w) => {
                    _setupViewModel.SelectedTabIndex = 1;
                    _setupViewModel.CheckProjectName = proj.Name;
                    NavigateToPage?.Invoke("SetupPage");
                }
            });

            // > edit ProjectName
            commands.Add(new CommandItem
            {
                Label = $"edit {localName}",
                Category = "project",
                Display = $"[>]  edit {displayName}",
                Action = (w) => {
                    _editorViewModel.SelectedProject = _editorViewModel.Projects.FirstOrDefault(p => p.Name == proj.Name && p.Tier == proj.Tier && p.Category == proj.Category);
                    NavigateToPage?.Invoke("EditorPage");
                }
            });

            // > term ProjectName
            commands.Add(new CommandItem
            {
                Label = $"term {localName}",
                Category = "project",
                Display = $"[>]  term {displayName}",
                Action = (w) => _shellService.OpenTerminal(localPath)
            });

            // > dir ProjectName
            commands.Add(new CommandItem
            {
                Label = $"dir {localName}",
                Category = "project",
                Display = $"[>]  dir {displayName}",
                Action = (w) => {
                    if (Directory.Exists(localPath))
                        _shellService.OpenFolder(localPath);
                }
            });

            // @ ProjectName (editor shortcut)
            commands.Add(new CommandItem
            {
                Label = localName,
                Category = "editor",
                Display = $"[@]  {displayName}",
                Action = (w) => {
                    _editorViewModel.SelectedProject = _editorViewModel.Projects.FirstOrDefault(p => p.Name == proj.Name && p.Tier == proj.Tier && p.Category == proj.Category);
                    NavigateToPage?.Invoke("EditorPage");
                }
            });

            // > timeline ProjectName
            commands.Add(new CommandItem
            {
                Label = $"timeline {localName}",
                Category = "project",
                Display = $"[>]  timeline {displayName}",
                Action = (w) => {
                    _timelineViewModel.SelectedProject = _timelineViewModel.Projects.FirstOrDefault(p => p.Name == proj.Name && p.Tier == proj.Tier && p.Category == proj.Category);
                    NavigateToPage?.Invoke("TimelinePage");
                }
            });

            // > resume ProjectName
            commands.Add(new CommandItem
            {
                Label = $"resume {localName}",
                Category = "project",
                Display = $"[>]  resume {displayName}",
                Action = async (w) => {
                    if (ShowResumeInputDialog != null)
                    {
                        var feature = await ShowResumeInputDialog(localName);
                        if (!string.IsNullOrWhiteSpace(feature))
                            InvokeResumeWork(localPath, feature);
                    }
                }
            });
        }

        _allCommands = commands;
    }

    private void AddTabCommand(List<CommandItem> commands, string label, string pageTypeName)
    {
        commands.Add(new CommandItem
        {
            Label = label,
            Category = "tab",
            Display = $"[Tab]  {label}",
            Action = (w) => NavigateToPage?.Invoke(pageTypeName)
        });
    }

    private void UpdateFilter()
    {
        string rawText = SearchText.Trim();
        string? categoryFilter = null;
        string searchText = rawText.ToLower();

        if (rawText.StartsWith(">"))
        {
            categoryFilter = "project";
            searchText = rawText.Substring(1).Trim().ToLower();
        }
        else if (rawText.StartsWith("@"))
        {
            categoryFilter = "editor";
            searchText = rawText.Substring(1).Trim().ToLower();
        }

        IEnumerable<CommandItem> filtered;
        if (string.IsNullOrEmpty(rawText))
        {
            filtered = _allCommands;
        }
        else if (categoryFilter != null && string.IsNullOrEmpty(searchText))
        {
            filtered = _allCommands.Where(c => c.Category == categoryFilter);
        }
        else
        {
            var tokens = searchText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            filtered = _allCommands.Where(c => {
                if (categoryFilter != null && c.Category != categoryFilter) return false;
                string label = c.Label.ToLower();
                return tokens.All(t => label.Contains(t));
            });

            filtered = filtered.OrderBy(c => {
                string label = c.Label.ToLower();
                if (label == searchText) return 0;
                if (label.StartsWith(searchText)) return 1;
                return 2;
            });
        }

        FilteredCommands.Clear();
        foreach (var cmd in filtered) FilteredCommands.Add(cmd);

        if (FilteredCommands.Count > 0)
        {
            SelectedCommand = FilteredCommands[0];
        }
        else
        {
            SelectedCommand = null;
        }
    }

    [RelayCommand]
    public void ExecuteCommand(object? window = null)
    {
        if (SelectedCommand != null)
        {
            var action = SelectedCommand.Action;
            Hide();
            action?.Invoke(window);
        }
    }

    private void InvokeResumeWork(string projPath, string featureName)
    {
        string safeFeature = Regex.Replace(featureName.Trim(), @"[\\/:*?""<>|]", "_");
        DateTime now = DateTime.Now;
        string yearStr = now.ToString("yyyy");
        string monthStr = now.ToString("yyyyMM");
        string dayStr = now.ToString("yyyyMMdd");

        string workDir = Path.Combine(projPath, "shared", "_work", yearStr, monthStr, $"{dayStr}_{safeFeature}");
        if (!Directory.Exists(workDir))
        {
            Directory.CreateDirectory(workDir);
        }

        _shellService.OpenFolder(workDir);
        _shellService.OpenTerminal(workDir);
    }
}
