using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using ProjectCurator.Desktop.Services;
using ProjectCurator.Desktop.Views.Pages;
using ProjectCurator.Interfaces;
using ProjectCurator.Models;
using ProjectCurator.Services;

namespace ProjectCurator.Desktop.Views;

public partial class MainWindow : Window
{
    private readonly IHotkeyService? _hotkeyService;
    private readonly ConfigService? _configService;
    private Dictionary<string, UserControl>? _pages;
    private UserControl? _currentPage;

    public MainWindow()
    {
        InitializeComponent();
        this.Loaded += OnLoaded;
        this.Closing += OnWindowClosing;
    }

    public MainWindow(IHotkeyService hotkeyService, ConfigService configService) : this()
    {
        _hotkeyService = hotkeyService;
        _configService = configService;
        if (_hotkeyService != null)
        {
            _hotkeyService.OnActivated = ToggleVisibility;
        }
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

        // Register global hotkey (Windows only)
        if (_hotkeyService is WindowsHotkeyService winHotkey)
        {
            var handle = TryGetPlatformHandle();
            if (handle != null)
                winHotkey.Register(handle.Handle);
        }
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        // Save window position/size before closing
        if (_configService != null)
            SaveWindowPosition(_configService);
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
        if (_pages == null) return;
        if (!_pages.TryGetValue(tag, out var page)) return;
        var content = this.FindControl<ContentControl>("PageContent");
        if (content != null)
            content.Content = page;
        _currentPage = page;
    }

    private void ToggleVisibility()
    {
        if (IsVisible) Hide();
        else { Show(); Activate(); }
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
                // TODO: show CommandPalette
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
}
