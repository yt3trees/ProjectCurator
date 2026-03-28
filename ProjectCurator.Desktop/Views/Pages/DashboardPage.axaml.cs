using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using ProjectCurator.Interfaces;
using ProjectCurator.Models;
using ProjectCurator.Services;
using ProjectCurator.ViewModels;
using ProjectCurator.Desktop.Views;
using System.Diagnostics;
using System.Text;

namespace ProjectCurator.Desktop.Views.Pages;

public partial class DashboardPage : UserControl
{
    private readonly DashboardViewModel? _viewModel;
    private readonly ConfigService? _configService;
    private readonly IShellService? _shellService;
    private readonly FileEncodingService? _fileEncodingService;

    public DashboardPage()
    {
        InitializeComponent();
        _viewModel = App.Services.GetService(typeof(DashboardViewModel)) as DashboardViewModel;
        _configService = App.Services.GetService(typeof(ConfigService)) as ConfigService;
        _shellService = App.Services.GetService(typeof(IShellService)) as IShellService;
        _fileEncodingService = App.Services.GetService(typeof(FileEncodingService)) as FileEncodingService;
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
            case "OpenVSCodeButton":
                if (button.Tag is ProjectCardViewModel vscodeCard)
                    _viewModel.OpenVSCode(vscodeCard);
                return;
            case "OpenWorkRootButton":
                if (button.Tag is ProjectCardViewModel workRootCard)
                    _viewModel.OpenWorkRoot(workRootCard);
                return;
            case "CreateTodayFolderButton":
                if (button.Tag is ProjectCardViewModel todayCard)
                    await CreateTodayGeneralWorkFolderAsync(todayCard);
                return;
            case "OpenEditorButton":
                if (button.Tag is ProjectCardViewModel editorCard)
                    _viewModel.OpenInEditor(editorCard);
                return;
            case "PinFolderButton":
                if (button.Tag is ProjectCardViewModel pinCard)
                    await PinGeneralWorkFolderAsync(pinCard);
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
            case "UncommittedBadgeButton":
                if (button.Tag is ProjectCardViewModel uncommittedCard)
                    await ShowUncommittedDetailsDialogAsync(uncommittedCard);
                return;
            case "ToggleWorkstreamButton":
                if (button.Tag is ProjectCardViewModel toggleCard)
                    toggleCard.IsWorkstreamExpanded = !toggleCard.IsWorkstreamExpanded;
                return;
            case "ShowClosedWorkstreamsButton":
                if (button.Tag is ProjectCardViewModel closedCard)
                    closedCard.ShowClosedWorkstreams = !closedCard.ShowClosedWorkstreams;
                return;
            case "WorkstreamFocusButton":
                if (button.Tag is WorkstreamCardItem focusWs)
                    await OpenWorkstreamFocusAsync(button, focusWs);
                return;
            case "WorkstreamWorkRootButton":
                if (button.Tag is WorkstreamCardItem workRootWs)
                {
                    var parentCard = FindAncestorDataContext<ProjectCardViewModel>(button);
                    if (parentCard != null)
                        _viewModel.OpenWorkstreamWorkRoot(parentCard, workRootWs);
                }
                return;
            case "WorkstreamTodayFolderButton":
                if (button.Tag is WorkstreamCardItem todayWs)
                {
                    var parentCard = FindAncestorDataContext<ProjectCardViewModel>(button);
                    if (parentCard != null)
                        await CreateTodayWorkstreamWorkFolderAsync(parentCard, todayWs);
                }
                return;
            case "WorkstreamPinFolderButton":
                if (button.Tag is WorkstreamCardItem pinWs)
                {
                    var parentCard = FindAncestorDataContext<ProjectCardViewModel>(button);
                    if (parentCard != null)
                        await PinWorkstreamWorkFolderAsync(parentCard, pinWs);
                }
                return;
            case "WorkstreamTerminalButton":
                if (button.Tag is WorkstreamCardItem termWs)
                {
                    var parentCard = FindAncestorDataContext<ProjectCardViewModel>(button);
                    if (parentCard != null)
                        _viewModel.OpenAgentTerminal(parentCard, termWs.Id);
                }
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

    // ===== Workstream helpers =====

    private Task OpenWorkstreamFocusAsync(Button button, WorkstreamCardItem ws)
    {
        if (string.IsNullOrWhiteSpace(ws.FocusFile) || !File.Exists(ws.FocusFile))
            return Task.CompletedTask;
        var parentCard = FindAncestorDataContext<ProjectCardViewModel>(button);
        if (parentCard == null) return Task.CompletedTask;
        var mainWindow = this.VisualRoot as MainWindow;
        mainWindow?.NavigateToEditorAndOpenFile(parentCard.Info, ws.FocusFile);
        return Task.CompletedTask;
    }

    private async Task CreateTodayGeneralWorkFolderAsync(ProjectCardViewModel card)
    {
        var featureName = await ShowWorkFolderFeatureDialogAsync($"Create General Work Folder ({card.Info.Name})");
        if (string.IsNullOrWhiteSpace(featureName)) return;

        var folder = _viewModel!.CreateTodayGeneralWorkFolder(card, featureName);
        if (!string.IsNullOrWhiteSpace(folder))
            _shellService?.OpenFolder(folder);
    }

    private async Task CreateTodayWorkstreamWorkFolderAsync(ProjectCardViewModel card, WorkstreamCardItem ws)
    {
        var featureName = await ShowWorkFolderFeatureDialogAsync($"Create Workstream Work Folder ({ws.Label})");
        if (string.IsNullOrWhiteSpace(featureName)) return;

        var folder = _viewModel!.CreateTodayWorkstreamWorkFolder(card, ws, featureName);
        if (!string.IsNullOrWhiteSpace(folder))
            _shellService?.OpenFolder(folder);
    }

    private async Task PinGeneralWorkFolderAsync(ProjectCardViewModel card)
    {
        if (_viewModel == null) return;
        var folders = await _viewModel.GetRecentWorkFoldersAsync(card.Info.Path, workstreamId: null);
        if (folders.Count == 0)
        {
            // Try auto-pin _work root
            var workRoot = Path.Combine(card.Info.Path, "shared", "_work");
            if (Directory.Exists(workRoot))
                _viewModel.PinFolder(card, workstream: null, folderName: "_work", fullPath: workRoot);
            return;
        }

        var picked = await ShowPinFolderPickerDialogAsync(
            $"Pin General Work Folder ({card.Info.Name})",
            folders,
            limit => _viewModel.GetRecentWorkFoldersAsync(card.Info.Path, null, limit));
        if (picked is null) return;

        _viewModel.PinFolder(card, workstream: null, picked.Value.FolderName, picked.Value.FullPath);
    }

    private async Task PinWorkstreamWorkFolderAsync(ProjectCardViewModel card, WorkstreamCardItem ws)
    {
        if (_viewModel == null) return;
        var folders = await _viewModel.GetRecentWorkFoldersAsync(card.Info.Path, ws.Id);
        if (folders.Count == 0)
            return;

        var picked = await ShowPinFolderPickerDialogAsync(
            $"Pin Workstream Folder ({ws.Label})",
            folders,
            limit => _viewModel.GetRecentWorkFoldersAsync(card.Info.Path, ws.Id, limit));
        if (picked is null) return;

        _viewModel.PinFolder(card, ws, picked.Value.FolderName, picked.Value.FullPath);
    }

    private void OpenCaptureLog()
    {
        if (_configService == null || _shellService == null)
            return;

        var captureLogPath = Path.Combine(_configService.ConfigDir, "capture_log.md");
        if (File.Exists(captureLogPath))
            _shellService.OpenFile(captureLogPath);
    }

    // ===== Dialogs =====

    private async Task<string?> ShowWorkFolderFeatureDialogAsync(string title)
    {
        var owner = this.VisualRoot as Window;
        if (owner == null) return null;

        var input = new TextBox
        {
            Watermark = "Enter folder suffix (e.g. meeting_notes)...",
            MinWidth = 360,
            FontSize = 13
        };

        var helper = new TextBlock
        {
            Text = "Folder name format: yyyyMMdd_xxx",
            Foreground = Brushes.Gray,
            FontSize = 12,
            Margin = new Avalonia.Thickness(0, 0, 0, 4)
        };

        var tcs = new TaskCompletionSource<string?>();
        var okBtn = new Button { Content = "Create", MinWidth = 110, IsDefault = true };
        var cancelBtn = new Button { Content = "Cancel", MinWidth = 90 };

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Avalonia.Thickness(0, 10, 0, 0),
            Children = { okBtn, cancelBtn }
        };

        var panel = new StackPanel
        {
            Margin = new Avalonia.Thickness(16),
            Spacing = 6,
            Children = { helper, input, footer }
        };

        var dialog = new Window
        {
            Title = title,
            Width = 480,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = panel
        };

        okBtn.Click += (_, _) =>
        {
            var val = input.Text?.Trim();
            if (string.IsNullOrWhiteSpace(val)) { input.Focus(); return; }
            tcs.TrySetResult(val);
            dialog.Close();
        };
        cancelBtn.Click += (_, _) => { tcs.TrySetResult(null); dialog.Close(); };
        dialog.Closed += (_, _) => tcs.TrySetResult(null);

        await dialog.ShowDialog(owner);
        return await tcs.Task;
    }

    private async Task<(string FolderName, string FullPath)?> ShowPinFolderPickerDialogAsync(
        string title,
        List<(string FolderName, string FullPath)> initialFolders,
        Func<int, Task<List<(string FolderName, string FullPath)>>>? loader = null)
    {
        var owner = this.VisualRoot as Window;
        if (owner == null) return null;

        const int step = 10;
        var currentFolders = initialFolders;

        var listBox = new ListBox
        {
            MinHeight = 80,
            MaxHeight = 220,
        };
        foreach (var (folderName, _) in currentFolders)
            listBox.Items.Add(folderName);
        if (listBox.Items.Count > 0)
            listBox.SelectedIndex = 0;

        var helper = new TextBlock
        {
            Text = "Select a folder to pin:",
            Foreground = Brushes.Gray,
            FontSize = 12
        };

        var showMoreBtn = new Button
        {
            Content = "Show more...",
            FontSize = 11,
            Padding = new Avalonia.Thickness(10, 2, 10, 2),
            Margin = new Avalonia.Thickness(0, 6, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            IsVisible = loader != null,
            IsEnabled = initialFolders.Count >= step
        };

        var tcs = new TaskCompletionSource<(string FolderName, string FullPath)?>();
        var pinBtn = new Button { Content = "Pin", MinWidth = 100, IsDefault = true };
        var cancelBtn = new Button { Content = "Cancel", MinWidth = 90 };

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Avalonia.Thickness(0, 10, 0, 0),
            Children = { pinBtn, cancelBtn }
        };

        var panel = new StackPanel
        {
            Margin = new Avalonia.Thickness(16),
            Spacing = 6,
            Children = { helper, listBox, showMoreBtn, footer }
        };

        var dialog = new Window
        {
            Title = title,
            Width = 440,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = panel
        };

        void CommitSelection()
        {
            if (listBox.SelectedIndex < 0) return;
            tcs.TrySetResult(currentFolders[listBox.SelectedIndex]);
            dialog.Close();
        }

        pinBtn.Click += (_, _) => CommitSelection();
        cancelBtn.Click += (_, _) => { tcs.TrySetResult(null); dialog.Close(); };
        dialog.Closed += (_, _) => tcs.TrySetResult(null);
        listBox.DoubleTapped += (_, _) => CommitSelection();

        showMoreBtn.Click += async (_, _) =>
        {
            if (loader is null) return;
            showMoreBtn.IsEnabled = false;
            var nextLimit = currentFolders.Count + step;
            var more = await loader(nextLimit);
            currentFolders = more;
            var prevIndex = listBox.SelectedIndex;
            listBox.Items.Clear();
            foreach (var (name, _) in currentFolders)
                listBox.Items.Add(name);
            listBox.SelectedIndex = prevIndex >= 0 ? prevIndex : 0;
            showMoreBtn.IsEnabled = more.Count >= nextLimit;
        };

        await dialog.ShowDialog(owner);
        return await tcs.Task;
    }

    // ===== Uncommitted details dialog =====

    private sealed class RepoStatusItem
    {
        public string RelativePath { get; init; } = ".";
        public string FullPath { get; init; } = "";
        public int TotalCount { get; init; }
        public int StagedCount { get; init; }
        public int ModifiedCount { get; init; }
        public int UntrackedCount { get; init; }
        public int ConflictCount { get; init; }
        public string Details { get; init; } = "";

        public string SummaryText
        {
            get
            {
                var parts = new List<string> { $"{TotalCount} changes" };
                if (StagedCount > 0) parts.Add($"staged {StagedCount}");
                if (ModifiedCount > 0) parts.Add($"modified {ModifiedCount}");
                if (UntrackedCount > 0) parts.Add($"untracked {UntrackedCount}");
                if (ConflictCount > 0) parts.Add($"conflicts {ConflictCount}");
                return string.Join(" | ", parts);
            }
        }

        public string DisplayLabel => $"{RelativePath}  ({SummaryText})";
    }

    private async Task ShowUncommittedDetailsDialogAsync(ProjectCardViewModel card)
    {
        var owner = this.VisualRoot as Window;
        if (owner == null) return;

        var items = await Task.Run(() => BuildUncommittedRepoStatusItems(card));

        var repoList = new ListBox
        {
            Margin = new Avalonia.Thickness(0, 4, 0, 0),
            MinHeight = 100,
            MaxHeight = 220
        };
        foreach (var item in items)
            repoList.Items.Add(item.DisplayLabel);

        var detailsBox = new TextBox
        {
            Margin = new Avalonia.Thickness(0, 4, 0, 0),
            MinHeight = 200,
            MaxHeight = 280,
            IsReadOnly = true,
            AcceptsReturn = true,
            FontFamily = new Avalonia.Media.FontFamily("Consolas,Monospace"),
            FontSize = 12,
            TextWrapping = TextWrapping.NoWrap
        };

        var selectedPathText = new TextBlock
        {
            Foreground = Brushes.Gray,
            FontSize = 11,
            Margin = new Avalonia.Thickness(0, 4, 0, 0),
            TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis
        };

        repoList.SelectionChanged += (_, _) =>
        {
            var idx = repoList.SelectedIndex;
            if (idx < 0 || idx >= items.Count) return;
            var sel = items[idx];
            detailsBox.Text = sel.Details;
            selectedPathText.Text = sel.FullPath;
        };

        var openFolderBtn = new Button { Content = "Open Folder", MinWidth = 110, IsEnabled = false };
        var closeBtn = new Button { Content = "Close", MinWidth = 90, IsCancel = true };
        var tcs = new TaskCompletionSource<bool>();

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Avalonia.Thickness(0, 8, 0, 0),
            Children = { openFolderBtn, closeBtn }
        };

        var panel = new StackPanel
        {
            Margin = new Avalonia.Thickness(14),
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = "Dirty repositories and current git status", Foreground = Brushes.Gray, FontSize = 12 },
                repoList,
                detailsBox,
                selectedPathText,
                footer
            }
        };

        var dialog = new Window
        {
            Title = $"Uncommitted Changes ({card.Info.Name})",
            Width = 740,
            Height = 640,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = panel
        };

        openFolderBtn.Click += (_, _) =>
        {
            var idx = repoList.SelectedIndex;
            if (idx < 0 || idx >= items.Count) return;
            var path = items[idx].FullPath;
            if (Directory.Exists(path))
                _shellService?.OpenFolder(path);
        };
        closeBtn.Click += (_, _) => { tcs.TrySetResult(true); dialog.Close(); };
        dialog.Closed += (_, _) => tcs.TrySetResult(true);

        if (items.Count > 0)
        {
            repoList.SelectedIndex = 0;
            openFolderBtn.IsEnabled = true;
        }
        else
        {
            detailsBox.Text = "No dirty repositories found.";
        }

        repoList.SelectionChanged += (_, _) =>
        {
            openFolderBtn.IsEnabled = repoList.SelectedIndex >= 0 &&
                                      repoList.SelectedIndex < items.Count &&
                                      Directory.Exists(items[repoList.SelectedIndex].FullPath);
        };

        await dialog.ShowDialog(owner);
        await tcs.Task;
    }

    private static List<RepoStatusItem> BuildUncommittedRepoStatusItems(ProjectCardViewModel card)
    {
        var devSource = Path.Combine(card.Info.Path, "development", "source");
        var relativePaths = card.Info.UncommittedRepoPaths
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p)
            .ToList();

        var results = new List<RepoStatusItem>();
        foreach (var relative in relativePaths)
        {
            var fullPath = relative == "."
                ? devSource
                : Path.Combine(devSource, relative.Replace('/', Path.DirectorySeparatorChar));

            var porcelainLines = RunGitStatusPorcelain(fullPath);
            var staged = 0;
            var modified = 0;
            var untracked = 0;
            var conflicts = 0;

            foreach (var line in porcelainLines)
            {
                if (line.Length < 2) continue;
                var x = line[0];
                var y = line[1];

                if (x == '?' && y == '?') { untracked++; continue; }
                if (x == 'U' || y == 'U') conflicts++;
                if (x != ' ' && x != '?') staged++;
                if (y != ' ' && y != '?') modified++;
            }

            results.Add(new RepoStatusItem
            {
                RelativePath = relative,
                FullPath = fullPath,
                TotalCount = porcelainLines.Count,
                StagedCount = staged,
                ModifiedCount = modified,
                UntrackedCount = untracked,
                ConflictCount = conflicts,
                Details = porcelainLines.Count > 0
                    ? string.Join(Environment.NewLine, porcelainLines)
                    : "(clean now)"
            });
        }

        return results;
    }

    private static List<string> RunGitStatusPorcelain(string repoPath)
    {
        if (!Directory.Exists(repoPath)) return [];

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = $"-c core.quotepath=false -C \"{repoPath}\" status --porcelain=1 --untracked-files=normal",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                }
            };

            process.Start();
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
                return [$"[git status error] {stderr.Trim()}"];

            return stdout
                .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
                .ToList();
        }
        catch (Exception ex)
        {
            return [$"[git status exception] {ex.Message}"];
        }
    }

    // ===== Visual tree helper =====

    private static T? FindAncestorDataContext<T>(Visual? start) where T : class
    {
        var current = start?.Parent as Visual;
        while (current != null)
        {
            if (current is StyledElement se && se.DataContext is T found)
                return found;
            current = current.Parent as Visual;
        }
        return null;
    }
}
