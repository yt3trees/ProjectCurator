using System.Diagnostics;
using System.IO;
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

    public DashboardPage(DashboardViewModel viewModel)
    {
        ViewModel = viewModel;
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

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
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
}
