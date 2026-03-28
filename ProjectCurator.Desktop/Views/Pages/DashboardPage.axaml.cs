using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ProjectCurator.Interfaces;
using ProjectCurator.Models;
using ProjectCurator.Services;
using ProjectCurator.ViewModels;

namespace ProjectCurator.Desktop.Views.Pages;

public partial class DashboardPage : UserControl
{
    private readonly DashboardViewModel? _viewModel;
    private readonly ConfigService? _configService;
    private readonly IShellService? _shellService;

    public DashboardPage()
    {
        InitializeComponent();
        _viewModel = App.Services.GetService(typeof(DashboardViewModel)) as DashboardViewModel;
        _configService = App.Services.GetService(typeof(ConfigService)) as ConfigService;
        _shellService = App.Services.GetService(typeof(IShellService)) as IShellService;
        DataContext = _viewModel;

        Loaded += OnLoaded;
        AddHandler(Button.ClickEvent, OnAnyButtonClick, RoutingStrategies.Bubble);
        AddHandler(Border.PointerPressedEvent, OnAnyBorderPressed, RoutingStrategies.Bubble);
        AutoRefreshComboBox.SelectionChanged += OnAutoRefreshChanged;
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (_viewModel == null)
            return;

        await _viewModel.RefreshAsync();
        _viewModel.SetupAutoRefresh();
    }

    private void OnAutoRefreshChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_viewModel == null)
            return;
        if (AutoRefreshComboBox.SelectedItem is not ComboBoxItem item)
            return;
        if (item.Tag is not string value || !int.TryParse(value, out var minutes))
            return;

        _viewModel.AutoRefreshMinutes = minutes;
        _viewModel.SetupAutoRefresh();
    }

    private async void OnAnyButtonClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel == null || e.Source is not Button button)
            return;

        switch (button.Name)
        {
            case "RefreshButton":
                await _viewModel.RefreshAsync(force: true);
                return;
            case "TodayQueueRefreshButton":
                await _viewModel.LoadTodayQueueAsync();
                return;
            case "UnsnoozeAllButton":
                await _viewModel.UnsnoozeAllAsync();
                return;
            case "ToggleHiddenButton":
                _viewModel.ShowHidden = !_viewModel.ShowHidden;
                return;
            case "HideProjectButton":
                if (button.Tag is ProjectCardViewModel hideCard)
                    _viewModel.HideProject(hideCard);
                return;
            case "UnhideProjectButton":
                if (button.Tag is ProjectCardViewModel unhideCard)
                    _viewModel.UnhideProject(unhideCard);
                return;
            case "OpenDirButton":
                if (button.Tag is ProjectCardViewModel dirCard)
                    _viewModel.OpenDirectory(dirCard);
                return;
            case "OpenTermButton":
                if (button.Tag is ProjectCardViewModel termCard)
                    _viewModel.OpenTerminal(termCard);
                return;
            case "OpenEditorButton":
                if (button.Tag is ProjectCardViewModel editorCard)
                    _viewModel.OpenInEditor(editorCard);
                return;
            case "PinFolderButton":
                if (button.Tag is ProjectCardViewModel pinCard)
                    await PinMostRecentWorkFolderAsync(pinCard);
                return;
            case "ClearPinnedButton":
                _viewModel.ClearPinnedFolders();
                return;
            case "OpenPinnedFolderButton":
                if (button.Tag is PinnedFolder pinnedFolder)
                    await _viewModel.OpenPinnedFolderAsync(pinnedFolder);
                return;
            case "UnpinFolderButton":
                if (button.Tag is PinnedFolder unpinTarget)
                    _viewModel.UnpinFolder(unpinTarget);
                return;
            case "OpenAsanaButton":
                if (button.Tag is TodayQueueTask openTask && !string.IsNullOrWhiteSpace(openTask.AsanaUrl))
                    _shellService?.OpenFile(openTask.AsanaUrl);
                return;
            case "SnoozeButton":
                if (button.Tag is TodayQueueTask snoozeTask)
                    await _viewModel.SnoozeTaskAsync(snoozeTask);
                return;
            case "DoneButton":
                if (button.Tag is TodayQueueTask doneTask)
                    await _viewModel.CompleteTaskAsync(doneTask);
                return;
            case "CaptureLogButton":
                OpenCaptureLog();
                return;
        }
    }

    private void OnAnyBorderPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_viewModel == null || e.Source is not Border border)
            return;
        if (border.Name != "ActivityBarBorder")
            return;

        var card = border.GetSelfAndVisualAncestors()
            .OfType<Control>()
            .Select(c => c.DataContext)
            .OfType<ProjectCardViewModel>()
            .FirstOrDefault();
        if (card != null)
            _viewModel.OpenInTimeline(card);
    }

    private async Task PinMostRecentWorkFolderAsync(ProjectCardViewModel card)
    {
        if (_viewModel == null)
            return;

        var recent = await _viewModel.GetRecentWorkFoldersAsync(card.Info.Path, workstreamId: null, limit: 1);
        if (recent.Count > 0)
        {
            var (name, fullPath) = recent[0];
            _viewModel.PinFolder(card, workstream: null, folderName: name, fullPath: fullPath);
            return;
        }

        var workRoot = Path.Combine(card.Info.Path, "shared", "_work");
        if (Directory.Exists(workRoot))
            _viewModel.PinFolder(card, workstream: null, folderName: "_work", fullPath: workRoot);
    }

    private void OpenCaptureLog()
    {
        if (_configService == null || _shellService == null)
            return;

        var captureLogPath = Path.Combine(_configService.ConfigDir, "capture_log.md");
        if (File.Exists(captureLogPath))
            _shellService.OpenFile(captureLogPath);
    }
}
