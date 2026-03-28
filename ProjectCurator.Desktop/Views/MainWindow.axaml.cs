using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using ProjectCurator.Desktop.Services;
using ProjectCurator.Desktop.Views.Controls;
using ProjectCurator.Desktop.Views.Pages;
using ProjectCurator.Interfaces;
using ProjectCurator.Models;
using ProjectCurator.Services;
using ProjectCurator.ViewModels;

namespace ProjectCurator.Desktop.Views;

public partial class MainWindow : Window
{
    private readonly IHotkeyService? _hotkeyService;
    private readonly ConfigService? _configService;
    private readonly CommandPaletteViewModel? _commandPaletteViewModel;
    private readonly DashboardViewModel? _dashboardViewModel;
    private readonly TimelineViewModel? _timelineViewModel;
    private readonly EditorViewModel? _editorViewModel;
    private Dictionary<string, UserControl>? _pages;
    private UserControl? _currentPage;
    private CaptureWindow? _activeCaptureWindow;
    private bool _allowClose;

    public MainWindow()
    {
        InitializeComponent();
        this.Loaded += OnLoaded;
        this.Closing += OnWindowClosing;
    }

    public MainWindow(
        IHotkeyService hotkeyService,
        ConfigService configService,
        CommandPaletteViewModel commandPaletteViewModel,
        DashboardViewModel dashboardViewModel,
        TimelineViewModel timelineViewModel,
        EditorViewModel editorViewModel) : this()
    {
        _hotkeyService = hotkeyService;
        _configService = configService;
        _commandPaletteViewModel = commandPaletteViewModel;
        _dashboardViewModel = dashboardViewModel;
        _timelineViewModel = timelineViewModel;
        _editorViewModel = editorViewModel;

        _hotkeyService.OnActivated = ToggleVisibility;
        _hotkeyService.OnCaptureActivated = ShowCaptureWindow;

        _commandPaletteViewModel.NavigateToPage = NavigateTo;
        _dashboardViewModel.OnOpenInEditor = NavigateToEditor;
        _dashboardViewModel.OnOpenInTimeline = NavigateToTimeline;
        _timelineViewModel.OnOpenFileInEditor = NavigateToEditorAndOpenFile;
    }

    private void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        InitializePages();
        NavigateTo("Dashboard");

        var navView = this.FindControl<NavigationView>("NavView");
        if (navView != null)
        {
            navView.SelectionChanged += OnNavSelectionChanged;
            // Select the Dashboard item initially
            if (navView.MenuItems.OfType<NavigationViewItem>().FirstOrDefault() is { } first)
                navView.SelectedItem = first;
        }

        // Restore window position/size from persisted state
        if (_configService != null)
            RestoreWindowPosition(_configService);

        // Register global hotkey
        if (_hotkeyService is WindowsHotkeyService winHotkey)
        {
            var handle = TryGetPlatformHandle();
            if (handle != null)
                winHotkey.Register(handle.Handle);
        }
        else if (_hotkeyService is MacOSHotkeyService macHotkey)
        {
            macHotkey.Register(IntPtr.Zero);
        }
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!_allowClose && !App.ExitRequested)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        // Save window position/size before closing
        if (_configService != null)
            SaveWindowPosition(_configService);

        _hotkeyService?.Unregister();
    }

    private void RestoreWindowPosition(ConfigService configService)
    {
        var placement = configService.LoadWindowPlacement();
        if (placement == null) return;

        if (placement.Width > 0 && placement.Height > 0)
        {
            Width = placement.Width;
            Height = placement.Height;
        }
        if (placement.Left != 0 || placement.Top != 0)
        {
            Position = new PixelPoint((int)placement.Left, (int)placement.Top);
        }
    }

    private void SaveWindowPosition(ConfigService configService)
    {
        configService.SaveWindowPlacement(new WindowPlacement
        {
            Left = Position.X,
            Top = Position.Y,
            Width = Width,
            Height = Height
        });
    }

    private void InitializePages()
    {
        _pages = new Dictionary<string, UserControl>
        {
            ["Dashboard"] = (UserControl)App.Services.GetRequiredService(typeof(DashboardPage)),
            ["Editor"]    = (UserControl)App.Services.GetRequiredService(typeof(EditorPage)),
            ["Timeline"]  = (UserControl)App.Services.GetRequiredService(typeof(TimelinePage)),
            ["GitRepos"]  = (UserControl)App.Services.GetRequiredService(typeof(GitReposPage)),
            ["AsanaSync"] = (UserControl)App.Services.GetRequiredService(typeof(AsanaSyncPage)),
            ["Setup"]     = (UserControl)App.Services.GetRequiredService(typeof(SetupPage)),
            ["Settings"]  = (UserControl)App.Services.GetRequiredService(typeof(SettingsPage)),
        };
    }

    private void OnNavSelectionChanged(object? sender, NavigationViewSelectionChangedEventArgs e)
    {
        if (e.SelectedItem is NavigationViewItem item && item.Tag is string tag)
            NavigateTo(tag);
    }

    private void NavigateTo(string tag)
    {
        tag = tag switch
        {
            "DashboardPage" => "Dashboard",
            "EditorPage" => "Editor",
            "TimelinePage" => "Timeline",
            "GitReposPage" => "GitRepos",
            "AsanaSyncPage" => "AsanaSync",
            "SetupPage" => "Setup",
            "SettingsPage" => "Settings",
            _ => tag
        };

        if (_pages == null) return;
        if (!_pages.TryGetValue(tag, out var page)) return;
        var content = this.FindControl<ContentControl>("PageContent");
        if (content != null)
            content.Content = page;

        var navView = this.FindControl<NavigationView>("NavView");
        if (navView != null)
        {
            var allItems = navView.MenuItems.OfType<NavigationViewItem>()
                .Concat(navView.FooterMenuItems.OfType<NavigationViewItem>());
            var selected = allItems.FirstOrDefault(i => string.Equals(i.Tag as string, tag, StringComparison.Ordinal));
            if (selected != null)
                navView.SelectedItem = selected;
        }

        _currentPage = page;
    }

    private void ToggleVisibility()
    {
        if (IsVisible) Hide();
        else { Show(); Activate(); }
    }

    public void RequestExit()
    {
        _allowClose = true;
        Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);

        if (ctrl)
        {
            var tag = e.Key switch
            {
                Key.D1 => "Dashboard",
                Key.D2 => "Editor",
                Key.D3 => "Timeline",
                Key.D4 => "GitRepos",
                Key.D5 => "AsanaSync",
                Key.D6 => "Setup",
                Key.D7 => "Settings",
                _ => null
            };
            if (tag != null)
            {
                NavigateTo(tag);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.K)
            {
                _commandPaletteViewModel?.Show();
                this.FindControl<CommandPaletteOverlay>("CommandPalette")?.FocusSearchBox();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.S)
            {
                // Save in EditorPage when Ctrl+S is pressed
                if (_currentPage is ProjectCurator.Desktop.Views.Pages.EditorPage editorPage &&
                    editorPage.DataContext is ProjectCurator.ViewModels.EditorViewModel editorVm)
                {
                    editorVm.SaveCommand.Execute(null);
                    e.Handled = true;
                    return;
                }
            }
        }

        if (e.Key == Key.Escape)
        {
            Hide();
            e.Handled = true;
        }
    }

    public void ShowCaptureWindow()
    {
        if (_activeCaptureWindow != null)
        {
            _activeCaptureWindow.Activate();
            return;
        }

        var captureWindow = new CaptureWindow
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            CanResize = false
        };

        captureWindow.Closed += (_, _) => _activeCaptureWindow = null;
        _activeCaptureWindow = captureWindow;
        _ = captureWindow.ShowDialog(this);
    }

    private void NavigateToEditor(ProjectInfo project)
    {
        if (_editorViewModel == null)
            return;

        var match = _editorViewModel.Projects.FirstOrDefault(p => p.HiddenKey == project.HiddenKey);
        _editorViewModel.SelectedProject = match ?? project;
        NavigateTo("Editor");
    }

    private void NavigateToTimeline(ProjectInfo project)
    {
        if (_timelineViewModel == null)
            return;

        _timelineViewModel.NavigateToProjectKey = project.HiddenKey;
        _timelineViewModel.SelectedProject = _timelineViewModel.Projects
            .FirstOrDefault(p => p.HiddenKey == project.HiddenKey);
        NavigateTo("Timeline");
    }

    private void NavigateToEditorAndOpenFile(ProjectInfo project, string filePath)
    {
        if (_editorViewModel == null)
            return;

        var match = _editorViewModel.Projects.FirstOrDefault(p => p.HiddenKey == project.HiddenKey);
        _editorViewModel.NavigateToProjectAndOpenFile(match ?? project, filePath);
        NavigateTo("Editor");
    }
}
