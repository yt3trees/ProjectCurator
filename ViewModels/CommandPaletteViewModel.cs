using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui;
using Wpf.Ui.Controls;
using ProjectCurator.Models;
using ProjectCurator.Services;
using ProjectCurator.Views.Pages;
using TextBlock = System.Windows.Controls.TextBlock;

namespace ProjectCurator.ViewModels;

public partial class CommandPaletteViewModel : ObservableObject
{
    private readonly ConfigService _configService;
    private readonly ProjectDiscoveryService _discoveryService;
    private readonly SetupViewModel _setupViewModel;
    private readonly EditorViewModel _editorViewModel;
    private readonly TimelineViewModel _timelineViewModel;
    private readonly StandupGeneratorService _standupGeneratorService;
    private readonly IContentDialogService _contentDialogService;

    [ObservableProperty]
    private bool isVisible;

    [ObservableProperty]
    private string searchText = "";

    [ObservableProperty]
    private ObservableCollection<CommandItem> filteredCommands = [];

    [ObservableProperty]
    private CommandItem? selectedCommand;

    private List<CommandItem> _allCommands = [];

    public CommandPaletteViewModel(
        ConfigService configService,
        ProjectDiscoveryService discoveryService,
        SetupViewModel setupViewModel,
        EditorViewModel editorViewModel,
        TimelineViewModel timelineViewModel,
        StandupGeneratorService standupGeneratorService,
        IContentDialogService contentDialogService)
    {
        _configService = configService;
        _discoveryService = discoveryService;
        _setupViewModel = setupViewModel;
        _editorViewModel = editorViewModel;
        _timelineViewModel = timelineViewModel;
        _standupGeneratorService = standupGeneratorService;
        _contentDialogService = contentDialogService;
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
        AddTabCommand(commands, "Dashboard", typeof(DashboardPage));
        AddTabCommand(commands, "Editor", typeof(EditorPage));
        AddTabCommand(commands, "Timeline", typeof(TimelinePage));
        AddTabCommand(commands, "Git Repos", typeof(GitReposPage));
        AddTabCommand(commands, "Asana Sync", typeof(AsanaSyncPage));
        AddTabCommand(commands, "Setup", typeof(SetupPage));
        AddTabCommand(commands, "Settings", typeof(SettingsPage));

        // --- Global commands ---
        commands.Add(new CommandItem
        {
            Label = "update focus",
            Category = "project",
            Display = "[>]  update focus (Update Focus from Asana)",
            Action = async (w) =>
            {
                w.RootNavigation.Navigate(typeof(EditorPage));
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
                Label = "meeting",
                Category = "project",
                Display = "[>]  meeting (Import Meeting Notes)",
                Action = async (w) =>
                {
                    w.RootNavigation.Navigate(typeof(EditorPage));
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
                        w.NavigateToEditorAndOpenFile(
                            new ProjectInfo { Name = "Standup", Path = Path.GetDirectoryName(standupPath) ?? "" },
                            standupPath);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Standup error: {ex.Message}");
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
                    _setupViewModel.SelectedTabIndex = 1; // Check tab
                    _setupViewModel.CheckProjectName = proj.Name;
                    w.RootNavigation.Navigate(typeof(SetupPage));
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
                    w.RootNavigation.Navigate(typeof(EditorPage));
                }
            });

            // > term ProjectName
            commands.Add(new CommandItem
            {
                Label = $"term {localName}",
                Category = "project",
                Display = $"[>]  term {displayName}",
                Action = (w) => OpenTerminalAtPath(localPath)
            });

            // > dir ProjectName
            commands.Add(new CommandItem
            {
                Label = $"dir {localName}",
                Category = "project",
                Display = $"[>]  dir {displayName}",
                Action = (w) => {
                    if (Directory.Exists(localPath))
                    {
                        Process.Start(new ProcessStartInfo("explorer.exe", localPath) { UseShellExecute = true });
                    }
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
                    w.RootNavigation.Navigate(typeof(EditorPage));
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
                    w.RootNavigation.Navigate(typeof(TimelinePage));
                }
            });

            // > resume ProjectName
            commands.Add(new CommandItem
            {
                Label = $"resume {localName}",
                Category = "project",
                Display = $"[>]  resume {displayName}",
                Action = async (w) => {
                    var feature = await ShowResumeDialogAsync(localName);
                    if (!string.IsNullOrWhiteSpace(feature)) {
                        InvokeResumeWork(localPath, feature);
                    }
                }
            });
        }

        _allCommands = commands;
    }

    private void AddTabCommand(List<CommandItem> commands, string label, Type pageType)
    {
        commands.Add(new CommandItem
        {
            Label = label,
            Category = "tab",
            Display = $"[Tab]  {label}",
            Action = (w) => w.RootNavigation.Navigate(pageType)
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

            // Sort: exact match > starts-with > contains
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
    public void ExecuteCommand(MainWindow window)
    {
        if (SelectedCommand != null)
        {
            var action = SelectedCommand.Action;
            Hide();
            action?.Invoke(window);
        }
    }

    private void OpenTerminalAtPath(string path)
    {
        if (!Directory.Exists(path)) return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "wt.exe",
                Arguments = $"-d \"{path}\"",
                UseShellExecute = true
            });
        }
        catch
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "pwsh.exe",
                    Arguments = $"-NoExit -Command \"Set-Location '{path}'\"",
                    UseShellExecute = true
                });
            }
            catch
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoExit -Command \"Set-Location '{path}'\"",
                        UseShellExecute = true
                    });
                }
                catch { }
            }
        }
    }

    private async Task<string?> ShowResumeDialogAsync(string projectName)
    {
        var inputControl = new Wpf.Ui.Controls.TextBox
        {
            PlaceholderText = "Feature name (e.g. fix-bug-123)",
            Margin = new Thickness(0, 8, 0, 0)
        };

        var dialog = new Wpf.Ui.Controls.ContentDialog
        {
            Title = $"Resume {projectName}",
            Content = new StackPanel
            {
                Children = {
                    new TextBlock { Text = "Enter feature name to create a new work directory:", Foreground = (System.Windows.Media.Brush)Application.Current.Resources["AppText"] },
                    inputControl
                }
            },
            PrimaryButtonText = "OK",
            CloseButtonText = "Cancel",
            DefaultButton = Wpf.Ui.Controls.ContentDialogButton.Primary
        };

        var result = await _contentDialogService.ShowAsync(dialog, default);
        if (result == Wpf.Ui.Controls.ContentDialogResult.Primary)
        {
            return inputControl.Text.Trim();
        }
        return null;
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

        Process.Start(new ProcessStartInfo("explorer.exe", workDir) { UseShellExecute = true });
        OpenTerminalAtPath(workDir);
    }
}
