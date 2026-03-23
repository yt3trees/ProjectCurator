using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Wpf.Ui.Controls;
using ProjectCurator.Models;
using ProjectCurator.Services;
using ProjectCurator.ViewModels;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace ProjectCurator.Views.Pages;

public partial class DashboardPage : WpfUserControl, INavigableView<DashboardViewModel>
{
    public DashboardViewModel ViewModel { get; }

    private readonly LlmClientService _llmClientService;
    private readonly FileEncodingService _fileEncodingService;

    public DashboardPage(DashboardViewModel viewModel, LlmClientService llmClientService, FileEncodingService fileEncodingService)
    {
        ViewModel = viewModel;
        _llmClientService = llmClientService;
        _fileEncodingService = fileEncodingService;
        DataContext = ViewModel;

        ViewModel.OnOpenInEditor = project =>
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.NavigateToEditor(project);
        };

        ViewModel.OnOpenInTimeline = project =>
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.NavigateToTimeline(project);
        };

        InitializeComponent();
        InitAutoRefreshCombo();
    }

    private void InitAutoRefreshCombo()
    {
        foreach (ComboBoxItem item in AutoRefreshComboBox.Items)
        {
            if (item.Tag is string s && s == ViewModel.AutoRefreshMinutes.ToString())
            {
                AutoRefreshComboBox.SelectedItem = item;
                return;
            }
        }
        AutoRefreshComboBox.SelectedIndex = 0; // Off
    }

    private bool _isInitialized = false;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_isInitialized) return;
        _isInitialized = true;
        await ViewModel.RefreshAsync();
        ViewModel.SetupAutoRefresh();
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e)
        => _ = ViewModel.RefreshAsync(force: true);

    private void OnAutoRefreshChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AutoRefreshComboBox.SelectedItem is ComboBoxItem item &&
            item.Tag is string s && int.TryParse(s, out int val))
        {
            ViewModel.AutoRefreshMinutes = val;
            ViewModel.SetupAutoRefresh();
        }
    }

    private void OnHideProject(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ProjectCardViewModel card })
            ViewModel.HideProject(card);
    }

    private void OnUnhideProject(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ProjectCardViewModel card })
            ViewModel.UnhideProject(card);
    }

    private void OnToggleShowHidden(object sender, RoutedEventArgs e)
        => ViewModel.ShowHidden = !ViewModel.ShowHidden;

    private void OnOpenDirClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ProjectCardViewModel card })
            ViewModel.OpenDirectory(card);
    }

    private void OnDirMenuVSCode(object sender, RoutedEventArgs e)
    {
        if (GetCardFromMenuItem(sender) is { } card)
            ViewModel.OpenVSCode(card);
    }

    private void OnDirMenuWorkRoot(object sender, RoutedEventArgs e)
    {
        if (GetCardFromMenuItem(sender) is { } card)
            ViewModel.OpenWorkRoot(card);
    }

    private async void OnDirMenuCreateTodayGeneralWork(object sender, RoutedEventArgs e)
    {
        if (GetCardFromMenuItem(sender) is not { } card) return;
        var featureName = await ShowWorkFolderFeatureDialogAsync($"Create General Work Folder ({card.Info.Name})");
        if (string.IsNullOrWhiteSpace(featureName)) return;

        var folder = ViewModel.CreateTodayGeneralWorkFolder(card, featureName);
        if (!string.IsNullOrWhiteSpace(folder))
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Dashboard] Failed to open folder: {ex}");
            }
        }
    }

    private void OnTermClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ProjectCardViewModel card })
            ViewModel.OpenTerminal(card);
    }

    private void OnTermMenuAgent(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.MenuItem mi) return;
        var agent = mi.Tag as string ?? "";
        if (GetCardFromMenuItem(sender) is { } card)
            ViewModel.OpenAgentTerminal(card, agent);
    }

    private static ProjectCardViewModel? GetCardFromMenuItem(object sender)
    {
        if (sender is System.Windows.Controls.MenuItem mi &&
            mi.Parent is System.Windows.Controls.ContextMenu cm &&
            cm.PlacementTarget is FrameworkElement fe &&
            fe.Tag is ProjectCardViewModel card)
            return card;
        return null;
    }

    private void OnOpenEditorClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ProjectCardViewModel card })
            ViewModel.OpenInEditor(card);
    }

    private void OnActivityBarClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ProjectCardViewModel card })
            ViewModel.OpenInTimeline(card);
    }

    private async void OnUncommittedBadgeClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ProjectCardViewModel card }) return;
        await ShowUncommittedDetailsDialogAsync(card);
    }

    private sealed class RepoStatusDialogItem
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
        var items = await Task.Run(() => BuildUncommittedRepoStatusItems(card));

        var appResources = Application.Current.Resources;
        var surface = (System.Windows.Media.Brush)appResources["AppSurface0"];
        var surface1 = (System.Windows.Media.Brush)appResources["AppSurface1"];
        var surface2 = (System.Windows.Media.Brush)appResources["AppSurface2"];
        var text = (System.Windows.Media.Brush)appResources["AppText"];
        var subtext = (System.Windows.Media.Brush)appResources["AppSubtext0"];
        var accent = appResources.Contains("AppPeach")
            ? (System.Windows.Media.Brush)appResources["AppPeach"]
            : text;

        var repoList = new System.Windows.Controls.ListBox
        {
            Margin = new Thickness(0, 8, 0, 0),
            MinHeight = 120,
            MaxHeight = 240,
            Background = surface1,
            Foreground = text,
            BorderBrush = surface2,
            BorderThickness = new Thickness(1),
            FontSize = 12,
            DisplayMemberPath = nameof(RepoStatusDialogItem.DisplayLabel)
        };
        foreach (var item in items) repoList.Items.Add(item);

        var detailsBox = new System.Windows.Controls.TextBox
        {
            Margin = new Thickness(0, 8, 0, 0),
            MinHeight = 210,
            MaxHeight = 290,
            IsReadOnly = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = surface1,
            Foreground = text,
            BorderBrush = surface2,
            BorderThickness = new Thickness(1),
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            FontSize = 12
        };

        var selectedRepoText = new System.Windows.Controls.TextBlock
        {
            Foreground = text,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 8, 0, 0)
        };

        var folderPathText = new System.Windows.Controls.TextBlock
        {
            Foreground = subtext,
            Margin = new Thickness(0, 6, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var helper = new System.Windows.Controls.TextBlock
        {
            Text = "Dirty repositories and current git status",
            Foreground = subtext,
            FontSize = 12
        };

        var titleBar = new Grid { Background = surface1, Height = 38 };
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleIcon = new System.Windows.Controls.TextBlock
        {
            Text = "●",
            Foreground = accent,
            FontSize = 11,
            Margin = new Thickness(12, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(titleIcon, 0);

        var titleText = new System.Windows.Controls.TextBlock
        {
            Text = $"Uncommitted Changes ({card.Info.Name})",
            Foreground = text,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(titleText, 1);

        var closeButton = new System.Windows.Controls.Button
        {
            Content = "✕",
            Width = 34,
            Height = 26,
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = System.Windows.Media.Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = subtext,
            FontSize = 13
        };
        Grid.SetColumn(closeButton, 2);

        titleBar.Children.Add(titleIcon);
        titleBar.Children.Add(titleText);
        titleBar.Children.Add(closeButton);

        var contentPanel = new System.Windows.Controls.StackPanel
        {
            Margin = new Thickness(16, 12, 16, 10),
            Children =
            {
                helper,
                repoList,
                selectedRepoText,
                detailsBox,
                folderPathText
            }
        };

        var openFolderButton = new Wpf.Ui.Controls.Button
        {
            Content = "Open Folder",
            Appearance = Wpf.Ui.Controls.ControlAppearance.Primary,
            MinWidth = 120,
            Height = 32,
            Margin = new Thickness(0, 0, 8, 0)
        };

        var closeFooterButton = new Wpf.Ui.Controls.Button
        {
            Content = "Close",
            Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary,
            MinWidth = 110,
            Height = 32,
            IsCancel = true
        };

        var footer = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(16, 0, 16, 12),
            Children = { openFolderButton, closeFooterButton }
        };

        var root = new Grid { Background = surface };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(titleBar, 0);
        Grid.SetRow(contentPanel, 1);
        Grid.SetRow(footer, 2);
        root.Children.Add(titleBar);
        root.Children.Add(contentPanel);
        root.Children.Add(footer);

        var owner = Window.GetWindow(this);
        var dialogWindow = new Window
        {
            Title = "",
            Owner = owner,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.None,
            MinWidth = 760,
            Width = 760,
            Height = 670,
            Background = surface,
            Foreground = text,
            BorderBrush = surface2,
            BorderThickness = new Thickness(1),
            Content = root
        };

        RepoStatusDialogItem? selectedItem = null;

        void ApplySelection(RepoStatusDialogItem? item)
        {
            selectedItem = item;
            if (item == null)
            {
                detailsBox.Text = "No dirty repositories found.";
                selectedRepoText.Text = "No repositories";
                folderPathText.Text = "";
                openFolderButton.IsEnabled = false;
                return;
            }

            selectedRepoText.Text = item.DisplayLabel;
            detailsBox.Text = item.Details;
            folderPathText.Text = item.FullPath;
            openFolderButton.IsEnabled = Directory.Exists(item.FullPath);
        }

        repoList.SelectionChanged += (_, _) =>
        {
            ApplySelection(repoList.SelectedItem as RepoStatusDialogItem);
        };

        openFolderButton.Click += (_, _) =>
        {
            if (selectedItem == null) return;
            if (!Directory.Exists(selectedItem.FullPath)) return;
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{selectedItem.FullPath}\"")
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Dashboard] Failed to open repo folder: {ex.Message}");
            }
        };

        closeButton.Click += (_, _) => dialogWindow.Close();
        closeFooterButton.Click += (_, _) => dialogWindow.Close();
        titleBar.MouseLeftButtonDown += (_, ev) =>
        {
            if (ev.LeftButton == MouseButtonState.Pressed) dialogWindow.DragMove();
        };

        if (items.Count > 0)
        {
            repoList.SelectedIndex = 0;
        }
        else
        {
            ApplySelection(null);
        }

        _ = dialogWindow.ShowDialog();
    }

    private static List<RepoStatusDialogItem> BuildUncommittedRepoStatusItems(ProjectCardViewModel card)
    {
        var devSource = Path.Combine(card.Info.Path, "development", "source");
        var relativePaths = card.Info.UncommittedRepoPaths
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p)
            .ToList();

        var results = new List<RepoStatusDialogItem>();
        foreach (var relative in relativePaths)
        {
            var fullPath = relative == "."
                ? devSource
                : Path.Combine(devSource, relative.Replace('/', '\\'));

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

                if (x == '?' && y == '?')
                {
                    untracked++;
                    continue;
                }

                if (x == 'U' || y == 'U')
                    conflicts++;

                if (x != ' ' && x != '?')
                    staged++;

                if (y != ' ' && y != '?')
                    modified++;
            }

            var details = porcelainLines.Count > 0
                ? string.Join(Environment.NewLine, porcelainLines)
                : "(clean now)";

            results.Add(new RepoStatusDialogItem
            {
                RelativePath = relative,
                FullPath = fullPath,
                TotalCount = porcelainLines.Count,
                StagedCount = staged,
                ModifiedCount = modified,
                UntrackedCount = untracked,
                ConflictCount = conflicts,
                Details = details
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
                    Arguments = $"-C \"{repoPath}\" status --porcelain=1 --untracked-files=normal",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                return [$"[git status error] {stderr.Trim()}"];
            }

            return stdout
                .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
                .ToList();
        }
        catch (Exception ex)
        {
            return [$"[git status exception] {ex.Message}"];
        }
    }

    private void OnTodayQueueRefreshClick(object sender, RoutedEventArgs e)
        => _ = ViewModel.LoadTodayQueueAsync();

    private void OnTodayQueueOpenAsana(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: TodayQueueTask task } &&
            !string.IsNullOrWhiteSpace(task.AsanaUrl))
        {
            try { Process.Start(new ProcessStartInfo(task.AsanaUrl) { UseShellExecute = true }); }
            catch { }
        }
    }

    private async void OnTodayQueueDoneClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: TodayQueueTask task }) return;

        var confirm = System.Windows.MessageBox.Show(
            $"Mark as done in Asana?\n[{task.ProjectShortName}] {task.Title}",
            "Confirm Done",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);
        if (confirm != System.Windows.MessageBoxResult.Yes) return;

        if (sender is UIElement btn) btn.IsEnabled = false;
        await ViewModel.CompleteTaskAsync(task);
    }

    private void OnTodayQueueSnoozeClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: TodayQueueTask task }) return;
        _ = ViewModel.SnoozeTaskAsync(task);
    }

    private void OnUnsnoozeAllClick(object sender, RoutedEventArgs e)
        => _ = ViewModel.UnsnoozeAllAsync();

    private void OnToggleWorkstream(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ProjectCardViewModel card })
            card.IsWorkstreamExpanded = !card.IsWorkstreamExpanded;
    }

    private void OnToggleShowClosedWorkstreams(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ProjectCardViewModel card })
            card.ShowClosedWorkstreams = !card.ShowClosedWorkstreams;
    }

    private void OnOpenWorkstreamFocusClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe) return;
        if (fe.DataContext is not WorkstreamCardItem ws) return;
        if (string.IsNullOrWhiteSpace(ws.FocusFile) || !File.Exists(ws.FocusFile)) return;

        var card = FindAncestorDataContext<ProjectCardViewModel>(fe);
        if (card == null) return;

        if (Window.GetWindow(this) is MainWindow mw)
            mw.NavigateToEditorAndOpenFile(card.Info, ws.FocusFile);
    }

    private void OnOpenWorkstreamWorkRootClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe) return;
        if (fe.DataContext is not WorkstreamCardItem ws) return;
        var card = FindAncestorDataContext<ProjectCardViewModel>(fe);
        if (card == null) return;
        ViewModel.OpenWorkstreamWorkRoot(card, ws);
    }

    private async void OnCreateWorkstreamTodayFolderClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe) return;
        if (fe.DataContext is not WorkstreamCardItem ws) return;
        var card = FindAncestorDataContext<ProjectCardViewModel>(fe);
        if (card == null) return;

        var featureName = await ShowWorkFolderFeatureDialogAsync($"Create Workstream Work Folder ({ws.Label})");
        if (string.IsNullOrWhiteSpace(featureName)) return;

        var folder = ViewModel.CreateTodayWorkstreamWorkFolder(card, ws, featureName);
        if (!string.IsNullOrWhiteSpace(folder))
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Dashboard] Failed to open folder: {ex}");
            }
        }
    }

    private Task<string?> ShowWorkFolderFeatureDialogAsync(string title)
    {
        var appResources = Application.Current.Resources;
        var surface = (System.Windows.Media.Brush)appResources["AppSurface0"];
        var surface1 = (System.Windows.Media.Brush)appResources["AppSurface1"];
        var surface2 = (System.Windows.Media.Brush)appResources["AppSurface2"];
        var text = (System.Windows.Media.Brush)appResources["AppText"];
        var subtext = (System.Windows.Media.Brush)appResources["AppSubtext0"];
        var accent = appResources.Contains("AppBlue")
            ? (System.Windows.Media.Brush)appResources["AppBlue"]
            : text;

        var input = new System.Windows.Controls.TextBox
        {
            Text = "",
            Margin = new Thickness(0, 10, 0, 0),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
            FontSize = 14,
            Padding = new Thickness(10, 7, 10, 7),
            Background = surface1,
            Foreground = text,
            BorderBrush = surface2,
            BorderThickness = new Thickness(1)
        };

        var helper = new System.Windows.Controls.TextBlock
        {
            Text = "Enter the suffix for folder name format yyyyMMdd_xxx:",
            Foreground = subtext,
            Margin = new Thickness(0, 0, 0, 0),
            FontSize = 13
        };

        var titleBar = new Grid
        {
            Background = surface1,
            Height = 38
        };
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleIcon = new System.Windows.Controls.TextBlock
        {
            Text = "◆",
            Foreground = accent,
            FontSize = 12,
            Margin = new Thickness(12, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(titleIcon, 0);

        var titleText = new System.Windows.Controls.TextBlock
        {
            Text = title,
            Foreground = text,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(titleText, 1);

        var closeButton = new System.Windows.Controls.Button
        {
            Content = "✕",
            Width = 34,
            Height = 26,
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = System.Windows.Media.Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = subtext,
            FontSize = 13
        };
        Grid.SetColumn(closeButton, 2);

        titleBar.Children.Add(titleIcon);
        titleBar.Children.Add(titleText);
        titleBar.Children.Add(closeButton);

        var contentPanel = new StackPanel
        {
            Margin = new Thickness(18, 14, 18, 10),
            Children =
            {
                helper,
                input
            }
        };

        var createButton = new Wpf.Ui.Controls.Button
        {
            Content = "Create",
            Appearance = Wpf.Ui.Controls.ControlAppearance.Primary,
            MinWidth = 120,
            Height = 34,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true
        };

        var cancelButton = new Wpf.Ui.Controls.Button
        {
            Content = "Cancel",
            Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary,
            MinWidth = 120,
            Height = 34,
            IsCancel = true
        };

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(18, 0, 18, 14),
            Children = { createButton, cancelButton }
        };

        var root = new Grid
        {
            Background = surface
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(titleBar, 0);
        Grid.SetRow(contentPanel, 1);
        Grid.SetRow(footer, 2);
        root.Children.Add(titleBar);
        root.Children.Add(contentPanel);
        root.Children.Add(footer);

        var owner = Window.GetWindow(this);
        var dialogWindow = new Window
        {
            Title = "",
            Owner = owner,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.Height,
            WindowStyle = WindowStyle.None,
            MinWidth = 500,
            MinHeight = 0,
            Width = 500,
            Background = surface,
            Foreground = text,
            BorderBrush = surface2,
            BorderThickness = new Thickness(1),
            Content = root
        };

        string? value = null;
        createButton.Click += (_, _) =>
        {
            var trimmed = input.Text.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                input.Focus();
                return;
            }
            value = trimmed;
            dialogWindow.DialogResult = true;
            dialogWindow.Close();
        };

        cancelButton.Click += (_, _) =>
        {
            dialogWindow.DialogResult = false;
            dialogWindow.Close();
        };

        closeButton.Click += (_, _) =>
        {
            dialogWindow.DialogResult = false;
            dialogWindow.Close();
        };

        titleBar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                dialogWindow.DragMove();
        };

        _ = dialogWindow.ShowDialog();
        return Task.FromResult(value);
    }

    // ===== Pinned Folders ドラッグ&ドロップ =====

    private System.Windows.Point _pinDragStart;

    // ボタン上ではドラッグを開始しない
    private static bool IsOverButton(RoutedEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        while (source != null)
        {
            if (source is System.Windows.Controls.Primitives.ButtonBase) return true;
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
    }

    private void OnPinnedChipMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (IsOverButton(e)) return;
        _pinDragStart = e.GetPosition(null);
    }

    private void OnPinnedChipMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (IsOverButton(e)) return;
        if (sender is not FrameworkElement { DataContext: PinnedFolder pf }) return;
        var diff = e.GetPosition(null) - _pinDragStart;
        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        DragDrop.DoDragDrop((DependencyObject)sender, pf, System.Windows.DragDropEffects.Move);
    }

    private void OnPinnedChipDrop(object sender, System.Windows.DragEventArgs e)
    {
        if (sender is Border b) b.ClearValue(Border.BorderBrushProperty);
        if (sender is not FrameworkElement { DataContext: PinnedFolder target }) return;
        if (e.Data.GetData(typeof(PinnedFolder)) is not PinnedFolder source) return;
        if (ReferenceEquals(source, target)) return;
        ViewModel.MovePinnedFolder(source, target);
    }

    private void OnPinnedChipDragEnter(object sender, System.Windows.DragEventArgs e)
    {
        if (sender is Border b && e.Data.GetDataPresent(typeof(PinnedFolder)))
            b.BorderBrush = (System.Windows.Media.Brush)Application.Current.Resources["AppBlue"];
    }

    private void OnPinnedChipDragLeave(object sender, System.Windows.DragEventArgs e)
    {
        if (sender is Border b)
            b.ClearValue(Border.BorderBrushProperty);
    }

    // ===== Pinned Folders イベントハンドラ =====

    private void OnOpenPinnedFolderClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: PinnedFolder pf })
            ViewModel.OpenPinnedFolder(pf);
    }

    private void OnUnpinFolderClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: PinnedFolder pf })
            ViewModel.UnpinFolder(pf);
    }

    private void OnClearPinnedFolders(object sender, RoutedEventArgs e)
        => ViewModel.ClearPinnedFolders();

    private async void OnPinWorkstreamFolderClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe) return;
        if (fe.DataContext is not WorkstreamCardItem ws) return;
        var card = FindAncestorDataContext<ProjectCardViewModel>(fe);
        if (card == null) return;

        var folders = await ViewModel.GetRecentWorkFoldersAsync(card.Info.Path, ws.Id);
        if (folders.Count == 0)
        {
            System.Windows.MessageBox.Show(
                "No recent work folders found.",
                "Pin Work Folder",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }

        var picked = await ShowPinFolderPickerDialogAsync(
            $"Pin Workstream Folder ({ws.Label})",
            folders,
            limit => ViewModel.GetRecentWorkFoldersAsync(card.Info.Path, ws.Id, limit));
        if (picked is null) return;

        ViewModel.PinFolder(card, ws, picked.Value.FolderName, picked.Value.FullPath);
    }

    private async void OnPinGeneralWorkFolderClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ProjectCardViewModel card }) return;
        await PinGeneralWorkFolderAsync(card);
    }

    private async void OnDirMenuPinGeneralWorkFolder(object sender, RoutedEventArgs e)
    {
        if (GetCardFromMenuItem(sender) is { } card)
            await PinGeneralWorkFolderAsync(card);
    }

    private async Task PinGeneralWorkFolderAsync(ProjectCardViewModel card)
    {
        var folders = await ViewModel.GetRecentWorkFoldersAsync(card.Info.Path, workstreamId: null);
        if (folders.Count == 0)
        {
            System.Windows.MessageBox.Show(
                "No recent work folders found.",
                "Pin Work Folder",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }

        var picked = await ShowPinFolderPickerDialogAsync(
            $"Pin General Work Folder ({card.Info.Name})",
            folders,
            limit => ViewModel.GetRecentWorkFoldersAsync(card.Info.Path, null, limit));
        if (picked is null) return;

        ViewModel.PinFolder(card, workstream: null, picked.Value.FolderName, picked.Value.FullPath);
    }

    private Task<(string FolderName, string FullPath)?> ShowPinFolderPickerDialogAsync(
        string title,
        List<(string FolderName, string FullPath)> initialFolders,
        Func<int, Task<List<(string FolderName, string FullPath)>>>? loader = null)
    {
        var appResources = Application.Current.Resources;
        var surface = (System.Windows.Media.Brush)appResources["AppSurface0"];
        var surface1 = (System.Windows.Media.Brush)appResources["AppSurface1"];
        var surface2 = (System.Windows.Media.Brush)appResources["AppSurface2"];
        var text = (System.Windows.Media.Brush)appResources["AppText"];
        var subtext = (System.Windows.Media.Brush)appResources["AppSubtext0"];
        var accent = appResources.Contains("AppOverlay2")
            ? (System.Windows.Media.Brush)appResources["AppOverlay2"]
            : text;

        // 現在のフォルダリスト (Show more で上書きされる)
        var currentFolders = initialFolders;
        const int step = 10;

        var listBox = new System.Windows.Controls.ListBox
        {
            Margin = new Thickness(0, 10, 0, 0),
            MinHeight = 80,
            MaxHeight = 220,
            Background = surface1,
            Foreground = text,
            BorderBrush = surface2,
            BorderThickness = new Thickness(1),
            FontSize = 12,
        };
        foreach (var (folderName, _) in currentFolders)
            listBox.Items.Add(folderName);
        if (listBox.Items.Count > 0) listBox.SelectedIndex = 0;

        var titleBar = new Grid { Background = surface1, Height = 38 };
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleIcon = new System.Windows.Controls.TextBlock
        {
            Text = "★", Foreground = accent, FontSize = 13,
            Margin = new Thickness(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(titleIcon, 0);

        var titleText = new System.Windows.Controls.TextBlock
        {
            Text = title, Foreground = text, FontSize = 14,
            FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(titleText, 1);

        var closeButton = new System.Windows.Controls.Button
        {
            Content = "✕", Width = 34, Height = 26,
            Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center,
            Background = System.Windows.Media.Brushes.Transparent,
            BorderThickness = new Thickness(0), Foreground = subtext, FontSize = 13
        };
        Grid.SetColumn(closeButton, 2);

        titleBar.Children.Add(titleIcon);
        titleBar.Children.Add(titleText);
        titleBar.Children.Add(closeButton);

        var helper = new System.Windows.Controls.TextBlock
        {
            Text = "Select a folder to pin:",
            Foreground = subtext, FontSize = 12,
        };

        var showMoreBtn = new Wpf.Ui.Controls.Button
        {
            Content = "Show more...",
            Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary,
            FontSize = 11,
            Height = 28,
            Padding = new Thickness(10, 0, 10, 0),
            Margin = new Thickness(0, 6, 0, 0),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Center,
            Visibility = loader != null ? Visibility.Visible : Visibility.Collapsed,
            IsEnabled = initialFolders.Count >= step,
        };

        var contentPanel = new StackPanel
        {
            Margin = new Thickness(16, 12, 16, 8),
            Children = { helper, listBox, showMoreBtn }
        };

        var pinButton = new Wpf.Ui.Controls.Button
        {
            Content = "Pin",
            Appearance = Wpf.Ui.Controls.ControlAppearance.Primary,
            MinWidth = 100, Height = 32,
            Margin = new Thickness(0, 0, 8, 0), IsDefault = true
        };
        var cancelButton = new Wpf.Ui.Controls.Button
        {
            Content = "Cancel",
            Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary,
            MinWidth = 100, Height = 32, IsCancel = true
        };

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(16, 0, 16, 12),
            Children = { pinButton, cancelButton }
        };

        var root = new Grid { Background = surface };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(titleBar, 0);
        Grid.SetRow(contentPanel, 1);
        Grid.SetRow(footer, 2);
        root.Children.Add(titleBar);
        root.Children.Add(contentPanel);
        root.Children.Add(footer);

        var owner = Window.GetWindow(this);
        var dialogWindow = new Window
        {
            Title = "",
            Owner = owner,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.Height,
            WindowStyle = WindowStyle.None,
            MinWidth = 420, Width = 420,
            Background = surface,
            Foreground = text,
            BorderBrush = surface2,
            BorderThickness = new Thickness(1),
            Content = root
        };

        (string FolderName, string FullPath)? result = null;

        pinButton.Click += (_, _) =>
        {
            if (listBox.SelectedIndex < 0) return;
            result = currentFolders[listBox.SelectedIndex];
            dialogWindow.DialogResult = true;
            dialogWindow.Close();
        };
        cancelButton.Click += (_, _) => { dialogWindow.DialogResult = false; dialogWindow.Close(); };
        closeButton.Click += (_, _) => { dialogWindow.DialogResult = false; dialogWindow.Close(); };
        listBox.MouseDoubleClick += (_, _) =>
        {
            if (listBox.SelectedIndex < 0) return;
            result = currentFolders[listBox.SelectedIndex];
            dialogWindow.DialogResult = true;
            dialogWindow.Close();
        };
        titleBar.MouseLeftButtonDown += (_, ev) =>
        {
            if (ev.LeftButton == MouseButtonState.Pressed) dialogWindow.DragMove();
        };

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
            // まだ取得できる可能性があれば再度有効化
            showMoreBtn.IsEnabled = more.Count >= nextLimit;
        };

        _ = dialogWindow.ShowDialog();
        return Task.FromResult(result);
    }

    private static T? FindAncestorDataContext<T>(DependencyObject start)
        where T : class
    {
        DependencyObject? cur = start;
        while (cur != null)
        {
            if (cur is FrameworkElement f && f.DataContext is T found)
                return found;
            cur = VisualTreeHelper.GetParent(cur);
        }
        return null;
    }

    // Today Queue 高さモード切り替え (Fixed ⇔ Resizable)
    private bool _isQueueResizable = false;
    private const double FixedQueueHeight = 220;
    private bool _isDraggingSplitter = false;
    private System.Windows.Point _splitterDragStart;
    private double _splitterDragStartRow4Height;

    private void OnQueueSplitterMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_isQueueResizable) return;
        _isDraggingSplitter = true;
        _splitterDragStart = e.GetPosition(RootGrid);
        _splitterDragStartRow4Height = RootGrid.RowDefinitions[4].ActualHeight;
        ((Border)sender).CaptureMouse();
        e.Handled = true;
    }

    private void OnQueueSplitterMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isDraggingSplitter) return;
        var current = e.GetPosition(RootGrid);
        var deltaY = current.Y - _splitterDragStart.Y;
        // 上ドラッグ (deltaY < 0) → Queue が大きくなる
        var newHeight = _splitterDragStartRow4Height - deltaY;
        newHeight = Math.Clamp(newHeight, 60, RootGrid.ActualHeight - 100);
        RootGrid.RowDefinitions[4].Height = new GridLength(newHeight);
        e.Handled = true;
    }

    private void OnQueueSplitterMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDraggingSplitter) return;
        _isDraggingSplitter = false;
        ((Border)sender).ReleaseMouseCapture();
        e.Handled = true;
    }

    private void OnToggleQueueResize(object sender, RoutedEventArgs e)
    {
        _isQueueResizable = !_isQueueResizable;

        if (_isQueueResizable)
        {
            // リサイズモード: GridSplitter を表示、高さを明示的に設定
            QueueSplitter.Visibility = Visibility.Visible;
            TodayQueueBorder.MaxHeight = double.PositiveInfinity;
            TodayQueueBorder.MinHeight = 60;
            RootGrid.RowDefinitions[4].Height = new GridLength(FixedQueueHeight);
            QueueResizeToggleBtn.Content = "📌";
            QueueResizeToggleBtn.ToolTip = "Switch to fixed height";
        }
        else
        {
            // 固定モード: GridSplitter を非表示、MaxHeight/MinHeight で高さを固定
            QueueSplitter.Visibility = Visibility.Collapsed;
            TodayQueueBorder.MaxHeight = FixedQueueHeight;
            TodayQueueBorder.MinHeight = FixedQueueHeight;
            RootGrid.RowDefinitions[4].Height = GridLength.Auto;
            QueueResizeToggleBtn.Content = "↕";
            QueueResizeToggleBtn.ToolTip = "Switch to resizable mode";
        }
    }

    // ===== What's Next =====

    private const string WhatsNextSystemPrompt = """
        You are a productivity assistant for a professional managing multiple parallel projects.
        Your job is to suggest the 3-5 most impactful actions they should take right now.

        ## Output rules
        - Return a JSON array of 3-5 suggestions, ordered by priority (highest first).
        - Each suggestion object:
          {
            "project": "exact project name",
            "action": "concise action (imperative, max 10 words)",
            "reason": "why this matters now (1-2 sentences)",
            "target_file": "relative file path if applicable, or null",
            "category": "task|focus|decision|commit|tension|review"
          }
        - Output ONLY the JSON array. No explanation, no markdown fences.

        ## Priority rules (in order)
        1. Overdue tasks - any task past its due date is highest priority
        2. Today-due tasks - tasks due today
        3. Stale focus - current_focus.md not updated in 7+ days with recent task activity
        4. Uncommitted changes - repos with changes that should be committed
        5. Unrecorded decisions - focus file mentions conclusions/choices without matching decision log
        6. Unresolved tensions - open items in tensions.md
        7. Upcoming tasks (1-2 days) - tasks due soon

        ## Category mapping
        - "task": Complete an overdue or due-today Asana task
        - "focus": Update current_focus.md (stale or missing)
        - "decision": Record a decision in decision_log
        - "commit": Commit or review uncommitted changes
        - "tension": Address an item in tensions.md
        - "review": Review or update project_summary.md

        ## Tone
        - Action-oriented, concise
        - Focus on outcomes ("Ship the migration script") not process ("Review the task list")
        - Include specific task/file names when available
        """;

    private sealed class WhatsNextSuggestion
    {
        public string Project { get; set; } = "";
        public string Action { get; set; } = "";
        public string Reason { get; set; } = "";
        public string? TargetFile { get; set; }
        public string Category { get; set; } = "";
    }

    private async void OnWhatsNextClickAsync(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.IsAiEnabled) return;

        var projects = ViewModel.Projects.Select(c => c.Info).ToList();
        if (projects.Count == 0)
        {
            System.Windows.MessageBox.Show(
                "No projects found. Please wait for the dashboard to load.",
                "What's Next",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }

        using var cts = new System.Threading.CancellationTokenSource();
        var loadingWindow = BuildWhatsNextLoadingWindow(cts);
        loadingWindow.Show();

        try
        {
            var focusPreviews = await CollectFocusPreviewsAsync(projects, cts.Token);
            var tasks = ViewModel.GetTopTasksForAi(30);
            var userPrompt = BuildWhatsNextUserPrompt(projects, tasks, focusPreviews);

            var lang = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            var systemPrompt = lang == "ja"
                ? WhatsNextSystemPrompt + "\n\nRespond entirely in Japanese."
                : WhatsNextSystemPrompt;

            var response = await _llmClientService.ChatCompletionAsync(
                systemPrompt, userPrompt, cts.Token);

            var suggestions = ParseWhatsNextResponse(response);

            if (loadingWindow.IsVisible) loadingWindow.Close();
            ShowWhatsNextResultsDialog(suggestions, response);
        }
        catch (OperationCanceledException)
        {
            // ユーザーキャンセル
        }
        catch (InvalidOperationException ex)
        {
            if (loadingWindow.IsVisible) loadingWindow.Close();
            System.Windows.MessageBox.Show(
                $"AI features error: {ex.Message}\n\nPlease check Settings > LLM API.",
                "What's Next - Error",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            if (loadingWindow.IsVisible) loadingWindow.Close();
            System.Windows.MessageBox.Show(
                $"Failed to get suggestions: {ex.Message}",
                "What's Next - Error",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    private async Task<Dictionary<string, string>> CollectFocusPreviewsAsync(
        List<ProjectInfo> projects, System.Threading.CancellationToken ct)
    {
        var result = new Dictionary<string, string>();
        foreach (var proj in projects)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(proj.FocusFile) || !File.Exists(proj.FocusFile))
            {
                result[proj.Name] = "(no focus file)";
                continue;
            }
            try
            {
                var (content, _) = await _fileEncodingService.ReadFileAsync(proj.FocusFile, ct);
                var preview = content.Length > 500 ? content[..500] : content;
                result[proj.Name] = preview.Replace("\r\n", "\n").Trim();
            }
            catch
            {
                result[proj.Name] = "(error reading focus file)";
            }
        }
        return result;
    }

    private static string BuildWhatsNextUserPrompt(
        List<ProjectInfo> projects,
        List<TodayQueueTask> tasks,
        Dictionary<string, string> focusPreviews)
    {
        var sb = new StringBuilder();
        var today = DateTime.Today;
        sb.AppendLine("## Date");
        sb.AppendLine($"{today:yyyy-MM-dd} ({today:dddd})");
        sb.AppendLine();

        sb.AppendLine("## Today Queue (sorted by priority)");
        if (tasks.Count == 0)
            sb.AppendLine("(no tasks)");
        else
            foreach (var t in tasks)
                sb.AppendLine($"- [{t.ProjectShortName}] {t.DisplayMainTitle} ({t.DueLabel})");
        sb.AppendLine();

        sb.AppendLine("## Project Signals");
        sb.AppendLine();
        foreach (var proj in projects)
        {
            var tensionsPath = Path.Combine(proj.AiContextContentPath, "tensions.md");
            int tensionsCount = 0;
            bool tensionsExists = File.Exists(tensionsPath);
            if (tensionsExists)
            {
                try
                {
                    tensionsCount = File.ReadAllLines(tensionsPath)
                        .Count(l => l.TrimStart().StartsWith("- ") || l.TrimStart().StartsWith("* "));
                }
                catch { }
            }

            var latestDecision = proj.DecisionLogDates.Count > 0
                ? proj.DecisionLogDates.Max().ToString("yyyy-MM-dd")
                : "none";

            var focusStale = proj.FocusAge.HasValue && proj.FocusAge > 7 ? " (stale)" : "";
            var uncommittedCount = proj.UncommittedRepoPaths.Count;

            sb.AppendLine($"### {proj.Name}");
            sb.AppendLine($"- Focus age: {(proj.FocusAge.HasValue ? $"{proj.FocusAge}d{focusStale}" : "missing")}");
            sb.AppendLine($"- Uncommitted repos: {uncommittedCount}");
            if (tensionsExists)
                sb.AppendLine($"- Open tensions: yes ({tensionsCount} items)");
            else
                sb.AppendLine("- Open tensions: no");
            sb.AppendLine($"- Recent decisions: {latestDecision}");
            if (focusPreviews.TryGetValue(proj.Name, out var preview) && !string.IsNullOrWhiteSpace(preview))
                sb.AppendLine($"- Focus preview: {preview}");
            sb.AppendLine();
        }

        sb.AppendLine("## Instruction");
        sb.AppendLine("Suggest 3-5 highest-priority actions based on the data above.");
        sb.AppendLine("Return JSON array only.");

        return sb.ToString();
    }

    private static List<WhatsNextSuggestion> ParseWhatsNextResponse(string response)
    {
        var json = response.Trim();
        if (json.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
            json = json[7..];
        else if (json.StartsWith("```"))
            json = json[3..];
        if (json.EndsWith("```"))
            json = json[..^3];
        json = json.Trim();

        try
        {
            var doc = JsonDocument.Parse(json);
            var suggestions = new List<WhatsNextSuggestion>();
            foreach (var elem in doc.RootElement.EnumerateArray())
            {
                suggestions.Add(new WhatsNextSuggestion
                {
                    Project = elem.TryGetProperty("project", out var p) ? p.GetString() ?? "" : "",
                    Action = elem.TryGetProperty("action", out var a) ? a.GetString() ?? "" : "",
                    Reason = elem.TryGetProperty("reason", out var r) ? r.GetString() ?? "" : "",
                    TargetFile = elem.TryGetProperty("target_file", out var tf) && tf.ValueKind != JsonValueKind.Null
                        ? tf.GetString() : null,
                    Category = elem.TryGetProperty("category", out var c) ? c.GetString() ?? "" : "",
                });
            }
            return suggestions;
        }
        catch
        {
            return [];
        }
    }

    private Window BuildWhatsNextLoadingWindow(System.Threading.CancellationTokenSource cts)
    {
        var appResources = Application.Current.Resources;
        var surface = (System.Windows.Media.Brush)appResources["AppSurface0"];
        var surface1 = (System.Windows.Media.Brush)appResources["AppSurface1"];
        var surface2 = (System.Windows.Media.Brush)appResources["AppSurface2"];
        var text = (System.Windows.Media.Brush)appResources["AppText"];
        var subtext = (System.Windows.Media.Brush)appResources["AppSubtext0"];

        var titleBar = new Grid { Background = surface1, Height = 38 };
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var titleIcon = new System.Windows.Controls.TextBlock
        {
            Text = "✨",
            FontSize = 13,
            Margin = new Thickness(12, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(titleIcon, 0);

        var titleText = new System.Windows.Controls.TextBlock
        {
            Text = "What's Next",
            Foreground = text,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(titleText, 1);

        titleBar.Children.Add(titleIcon);
        titleBar.Children.Add(titleText);

        var loadingText = new System.Windows.Controls.TextBlock
        {
            Text = "Analyzing your projects...",
            Foreground = subtext,
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 14),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center
        };

        var cancelBtn = new Wpf.Ui.Controls.Button
        {
            Content = "Cancel",
            Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary,
            MinWidth = 100,
            Height = 32,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center
        };

        var content = new StackPanel
        {
            Margin = new Thickness(24, 18, 24, 18),
            Children = { loadingText, cancelBtn }
        };

        var root = new Grid { Background = surface };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(titleBar, 0);
        Grid.SetRow(content, 1);
        root.Children.Add(titleBar);
        root.Children.Add(content);

        var owner = Window.GetWindow(this);
        var win = new Window
        {
            Title = "",
            Owner = owner,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.Height,
            WindowStyle = WindowStyle.None,
            Width = 340,
            Background = surface,
            Foreground = text,
            BorderBrush = surface2,
            BorderThickness = new Thickness(1),
            Content = root
        };

        cancelBtn.Click += (_, _) => { cts.Cancel(); win.Close(); };
        titleBar.MouseLeftButtonDown += (_, ev) =>
        {
            if (ev.LeftButton == MouseButtonState.Pressed) win.DragMove();
        };

        return win;
    }

    private void ShowWhatsNextResultsDialog(List<WhatsNextSuggestion> suggestions, string rawResponse)
    {
        var mw = Window.GetWindow(this) as MainWindow;

        var appResources = Application.Current.Resources;
        var surface = (System.Windows.Media.Brush)appResources["AppSurface0"];
        var surface1 = (System.Windows.Media.Brush)appResources["AppSurface1"];
        var surface2 = (System.Windows.Media.Brush)appResources["AppSurface2"];
        var text = (System.Windows.Media.Brush)appResources["AppText"];
        var subtext = (System.Windows.Media.Brush)appResources["AppSubtext0"];
        var accent = appResources.Contains("AppBlue")
            ? (System.Windows.Media.Brush)appResources["AppBlue"]
            : text;

        var titleBar = new Grid { Background = surface1, Height = 38 };
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleIcon = new System.Windows.Controls.TextBlock
        {
            Text = "✨",
            FontSize = 13,
            Margin = new Thickness(12, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(titleIcon, 0);

        var titleText = new System.Windows.Controls.TextBlock
        {
            Text = "What's Next",
            Foreground = text,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(titleText, 1);

        var closeButton = new System.Windows.Controls.Button
        {
            Content = "✕",
            Width = 34,
            Height = 26,
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = System.Windows.Media.Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = subtext,
            FontSize = 13
        };
        Grid.SetColumn(closeButton, 2);

        titleBar.Children.Add(titleIcon);
        titleBar.Children.Add(titleText);
        titleBar.Children.Add(closeButton);

        var suggestionsPanel = new StackPanel();
        Window? dialogWindow = null;

        if (suggestions.Count == 0)
        {
            var fallback = new System.Windows.Controls.TextBox
            {
                Text = string.IsNullOrWhiteSpace(rawResponse) ? "(No suggestions)" : rawResponse,
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                Background = surface1,
                Foreground = text,
                BorderBrush = surface2,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8),
                FontSize = 12,
                Margin = new Thickness(16, 12, 16, 8),
                MaxHeight = 400
            };
            suggestionsPanel.Children.Add(fallback);
        }
        else
        {
            for (int i = 0; i < suggestions.Count; i++)
            {
                var suggestion = suggestions[i];

                if (i > 0)
                    suggestionsPanel.Children.Add(new Border
                    {
                        Height = 1,
                        Background = surface2,
                        Margin = new Thickness(16, 0, 16, 0)
                    });

                var cardGrid = new Grid { Margin = new Thickness(16, 10, 16, 10) };
                cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var numBlock = new System.Windows.Controls.TextBlock
                {
                    Text = $"{i + 1}.",
                    Foreground = subtext,
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 2, 10, 0),
                    MinWidth = 22
                };
                Grid.SetColumn(numBlock, 0);

                var bodyPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Top };

                var projectHeader = new System.Windows.Controls.TextBlock
                {
                    Text = $"[{suggestion.Project}]",
                    Foreground = accent,
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };

                var actionBlock = new System.Windows.Controls.TextBlock
                {
                    Text = suggestion.Action,
                    Foreground = text,
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 0, 0)
                };

                var reasonBlock = new System.Windows.Controls.TextBlock
                {
                    Text = suggestion.Reason,
                    Foreground = subtext,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 3, 0, 0)
                };

                bodyPanel.Children.Add(projectHeader);
                bodyPanel.Children.Add(actionBlock);
                bodyPanel.Children.Add(reasonBlock);
                Grid.SetColumn(bodyPanel, 1);

                var openBtn = new Wpf.Ui.Controls.Button
                {
                    Content = "Open ▶",
                    Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary,
                    FontSize = 11,
                    Height = 28,
                    Padding = new Thickness(10, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(10, 0, 0, 0)
                };
                Grid.SetColumn(openBtn, 2);

                var capturedSuggestion = suggestion;
                openBtn.Click += (_, _) =>
                {
                    NavigateToSuggestionDirect(mw, capturedSuggestion);
                    mw?.Activate();
                };

                cardGrid.Children.Add(numBlock);
                cardGrid.Children.Add(bodyPanel);
                cardGrid.Children.Add(openBtn);
                suggestionsPanel.Children.Add(cardGrid);
            }
        }

        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = 480,
            Content = suggestionsPanel
        };

        var copyBtn = new Wpf.Ui.Controls.Button
        {
            Content = "Copy",
            Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary,
            MinWidth = 90,
            Height = 32,
            Margin = new Thickness(0, 0, 8, 0)
        };

        var logBtn = new Wpf.Ui.Controls.Button
        {
            Content = "Debug Log",
            Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary,
            Height = 32,
            Padding = new Thickness(10, 0, 10, 0),
            ToolTip = "Show LLM prompt log"
        };

        var closeFooterBtn = new Wpf.Ui.Controls.Button
        {
            Content = "Close",
            Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary,
            MinWidth = 90,
            Height = 32
        };

        var footerGrid = new Grid { Margin = new Thickness(16, 8, 16, 12) };
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Grid.SetColumn(logBtn, 0);

        var rightBtns = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Children = { copyBtn, closeFooterBtn }
        };
        Grid.SetColumn(rightBtns, 2);

        footerGrid.Children.Add(logBtn);
        footerGrid.Children.Add(rightBtns);

        var root = new Grid { Background = surface };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(titleBar, 0);
        Grid.SetRow(scrollViewer, 1);
        Grid.SetRow(footerGrid, 2);
        root.Children.Add(titleBar);
        root.Children.Add(scrollViewer);
        root.Children.Add(footerGrid);

        var owner = Window.GetWindow(this);
        dialogWindow = new Window
        {
            Title = "",
            Owner = owner,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.Height,
            WindowStyle = WindowStyle.None,
            MinWidth = 680,
            Width = 680,
            Background = surface,
            Foreground = text,
            BorderBrush = surface2,
            BorderThickness = new Thickness(1),
            Content = root
        };

        copyBtn.Click += (_, _) =>
        {
            var clipText = BuildWhatsNextClipboardText(suggestions, rawResponse);
            System.Windows.Clipboard.SetText(clipText);
        };

        logBtn.Click += (_, _) =>
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== SYSTEM PROMPT ===");
            sb.AppendLine(_llmClientService.LastSystemPrompt);
            sb.AppendLine();
            sb.AppendLine("=== USER PROMPT ===");
            sb.AppendLine(_llmClientService.LastUserPrompt);
            sb.AppendLine();
            sb.AppendLine("=== RESPONSE ===");
            sb.AppendLine(_llmClientService.LastResponse);
            ShowWhatsNextLogDialog("LLM Debug Log", sb.ToString());
        };

        closeButton.Click += (_, _) => dialogWindow.Close();
        closeFooterBtn.Click += (_, _) => dialogWindow.Close();
        titleBar.MouseLeftButtonDown += (_, ev) =>
        {
            if (ev.LeftButton == MouseButtonState.Pressed) dialogWindow.DragMove();
        };

        dialogWindow.Show();
    }

    private void ShowWhatsNextLogDialog(string title, string message)
    {
        var appResources = Application.Current.Resources;
        var surface  = (System.Windows.Media.Brush)appResources["AppSurface0"];
        var surface1 = (System.Windows.Media.Brush)appResources["AppSurface1"];
        var surface2 = (System.Windows.Media.Brush)appResources["AppSurface2"];
        var text     = (System.Windows.Media.Brush)appResources["AppText"];
        var subtext  = (System.Windows.Media.Brush)appResources["AppSubtext0"];

        var titleBar = new Grid { Background = surface1, Height = 38 };
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleIcon = new Wpf.Ui.Controls.SymbolIcon
        {
            Symbol = Wpf.Ui.Controls.SymbolRegular.Bug24,
            FontSize = 16,
            Foreground = text,
            Margin = new Thickness(12, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(titleIcon, 0);

        var titleText = new System.Windows.Controls.TextBlock
        {
            Text = title,
            Foreground = text,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(titleText, 1);

        var closeBtn = new System.Windows.Controls.Button
        {
            Content = "✕", Width = 34, Height = 26,
            Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center,
            Background = System.Windows.Media.Brushes.Transparent,
            BorderThickness = new Thickness(0), Foreground = subtext, FontSize = 13
        };
        Grid.SetColumn(closeBtn, 2);

        titleBar.Children.Add(titleIcon);
        titleBar.Children.Add(titleText);
        titleBar.Children.Add(closeBtn);

        var textBox = new System.Windows.Controls.TextBox
        {
            Text = message,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = surface1,
            Foreground = text,
            BorderBrush = surface2,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8),
            FontFamily = new System.Windows.Media.FontFamily("Consolas, 'Courier New', monospace"),
            FontSize = 12,
            MinHeight = 80,
            MaxHeight = 400
        };

        var okButton = new Wpf.Ui.Controls.Button
        {
            Content = "OK",
            Appearance = Wpf.Ui.Controls.ControlAppearance.Primary,
            MinWidth = 80, Height = 32,
            IsDefault = true, IsCancel = true
        };

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(16, 0, 16, 12),
            Children = { okButton }
        };

        var contentPanel = new StackPanel { Margin = new Thickness(16, 12, 16, 8) };
        contentPanel.Children.Add(textBox);

        var root = new Grid { Background = surface };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(titleBar, 0);
        Grid.SetRow(contentPanel, 1);
        Grid.SetRow(footer, 2);
        root.Children.Add(titleBar);
        root.Children.Add(contentPanel);
        root.Children.Add(footer);

        var owner = Window.GetWindow(this);
        var logWindow = new Window
        {
            Title = "",
            Owner = owner,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.Height,
            WindowStyle = WindowStyle.None,
            MinWidth = 680,
            Width = 680,
            Background = surface,
            Foreground = text,
            BorderBrush = surface2,
            BorderThickness = new Thickness(1),
            Content = root
        };

        closeBtn.Click += (_, _) => logWindow.Close();
        okButton.Click += (_, _) => logWindow.Close();
        titleBar.MouseLeftButtonDown += (_, ev) =>
        {
            if (ev.LeftButton == MouseButtonState.Pressed) logWindow.DragMove();
        };

        _ = logWindow.ShowDialog();
    }

    private void NavigateToSuggestionDirect(MainWindow? mw, WhatsNextSuggestion suggestion)
    {
        if (mw == null) return;

        var card = ViewModel.Projects.FirstOrDefault(c =>
            string.Equals(c.Info.Name, suggestion.Project, StringComparison.OrdinalIgnoreCase));
        if (card == null) return;

        if (!string.IsNullOrWhiteSpace(suggestion.TargetFile))
        {
            var fullPath = ResolveWhatsNextTargetFile(card.Info, suggestion.TargetFile);
            if (!string.IsNullOrWhiteSpace(fullPath) && File.Exists(fullPath))
            {
                mw.NavigateToEditorAndOpenFile(card.Info, fullPath);
                return;
            }
        }

        mw.NavigateToEditor(card.Info);
    }

    private static string? ResolveWhatsNextTargetFile(ProjectInfo project, string targetFile)
    {
        if (Path.IsPathRooted(targetFile))
            return targetFile;

        var candidate1 = Path.Combine(project.AiContextContentPath, targetFile);
        if (File.Exists(candidate1)) return candidate1;

        var candidate2 = Path.Combine(project.Path, targetFile);
        if (File.Exists(candidate2)) return candidate2;

        return null;
    }

    private static string BuildWhatsNextClipboardText(List<WhatsNextSuggestion> suggestions, string rawResponse)
    {
        if (suggestions.Count == 0)
            return rawResponse;

        var sb = new StringBuilder();
        sb.AppendLine("What's Next:");
        for (int i = 0; i < suggestions.Count; i++)
        {
            var s = suggestions[i];
            sb.AppendLine($"{i + 1}. [{s.Project}] {s.Action}");
            if (!string.IsNullOrWhiteSpace(s.Reason))
                sb.AppendLine($"   {s.Reason}");
        }
        return sb.ToString().TrimEnd();
    }
}
