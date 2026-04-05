using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Curia.Models;
using Curia.Services;

using Button = System.Windows.Controls.Button;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace Curia.Views;

internal static class TeamViewDialog
{
    public static void ShowDialog(
        Window? owner,
        string projectName,
        string obsidianProjectPath,
        TeamViewConfig teamView,
        AsanaSyncService asanaSyncService,
        TeamTaskParser teamTaskParser)
    {
        var appResources = Application.Current.Resources;
        var surface  = (Brush)appResources["AppSurface0"];
        var surface1 = (Brush)appResources["AppSurface1"];
        var surface2 = (Brush)appResources["AppSurface2"];
        var text     = (Brush)appResources["AppText"];
        var subtext  = (Brush)appResources["AppSubtext0"];
        var blue     = appResources.Contains("AppBlue") ? (Brush)appResources["AppBlue"] : surface2;
        var red      = appResources.Contains("AppRed") ? (Brush)appResources["AppRed"] : Brushes.OrangeRed;

        var teamTasksFilePath = System.IO.Path.Combine(obsidianProjectPath, "team-tasks.md");
        Window dialogWindow = null!;

        // ===== タイトルバー =====
        var titleBar = new Grid { Background = surface1, Height = 38 };
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleText = new TextBlock
        {
            Text = $"Team View - {projectName}",
            Foreground = text,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0)
        };
        Grid.SetColumn(titleText, 0);
        Grid.SetColumnSpan(titleText, 2);

        var syncBtn = new Wpf.Ui.Controls.Button
        {
            Content = "Sync",
            Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary,
            Height = 26,
            Padding = new Thickness(12, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        Grid.SetColumn(syncBtn, 2);

        var closeBtn = new Button
        {
            Content = "✕",
            Width = 34,
            Height = 26,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = subtext,
            FontSize = 13,
            IsCancel = true
        };
        Grid.SetColumn(closeBtn, 3);

        titleBar.Children.Add(titleText);
        titleBar.Children.Add(syncBtn);
        titleBar.Children.Add(closeBtn);

        // ===== Last Sync / ステータス =====
        var statusText = new TextBlock
        {
            Foreground = subtext,
            FontSize = 11,
            Margin = new Thickness(12, 4, 12, 4)
        };

        // ===== カードエリア =====
        var scrollView = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(8, 4, 8, 8)
        };

        var membersPanel = new WrapPanel
        {
            Orientation = Orientation.Horizontal
        };
        scrollView.Content = membersPanel;

        // ===== ルートレイアウト =====
        var root = new Grid { Background = surface };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // titleBar
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // statusText
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // scrollView

        Grid.SetRow(titleBar, 0);
        Grid.SetRow(statusText, 1);
        Grid.SetRow(scrollView, 2);

        root.Children.Add(titleBar);
        root.Children.Add(statusText);
        root.Children.Add(scrollView);

        // ===== ダイアログウィンドウ =====
        dialogWindow = new Window
        {
            Title = "",
            Owner = owner,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize,
            WindowStyle = WindowStyle.None,
            MinWidth = 560,
            MinHeight = 400,
            Width = 960,
            Height = 600,
            Background = surface,
            Foreground = text,
            BorderBrush = surface2,
            BorderThickness = new Thickness(1),
            Content = root
        };

        System.Windows.Shell.WindowChrome.SetWindowChrome(dialogWindow,
            new System.Windows.Shell.WindowChrome
            {
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(4),
                GlassFrameThickness = new Thickness(0),
                UseAeroCaptionButtons = false
            });

        // ===== 初期データ表示 =====
        LoadAndDisplayTeamTasks(membersPanel, statusText, teamTasksFilePath, teamTaskParser, appResources, text, subtext, red, blue);

        // ===== イベント =====
        closeBtn.Click += (_, _) => dialogWindow.Close();

        titleBar.MouseLeftButtonDown += (_, ev) =>
        {
            if (ev.LeftButton == MouseButtonState.Pressed) dialogWindow.DragMove();
        };

        syncBtn.Click += async (_, _) =>
        {
            syncBtn.IsEnabled = false;
            statusText.Text = "Syncing...";
            statusText.Foreground = blue;
            try
            {
                await Task.Run(async () =>
                    await asanaSyncService.RunTeamSyncAsync(
                        obsidianProjectPath,
                        projectName,
                        teamView,
                        line => Application.Current.Dispatcher.InvokeAsync(
                            () => statusText.Text = line.TrimEnd())));

                Application.Current.Dispatcher.Invoke(() =>
                    LoadAndDisplayTeamTasks(membersPanel, statusText, teamTasksFilePath, teamTaskParser, appResources, text, subtext, red, blue));
            }
            catch (Exception ex)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    statusText.Text = $"Sync error: {ex.Message}";
                    statusText.Foreground = red;
                });
            }
            finally
            {
                Application.Current.Dispatcher.Invoke(() => syncBtn.IsEnabled = true);
            }
        };

        dialogWindow.ShowDialog();
    }

    private static void LoadAndDisplayTeamTasks(
        WrapPanel membersPanel,
        TextBlock statusText,
        string filePath,
        TeamTaskParser parser,
        ResourceDictionary appResources,
        Brush text,
        Brush subtext,
        Brush redBrush,
        Brush blueBrush)
    {
        var surface1 = (Brush)appResources["AppSurface1"];
        var surface2 = (Brush)appResources["AppSurface2"];

        var (members, lastSync) = parser.Parse(filePath);

        membersPanel.Children.Clear();

        if (members.Count == 0)
        {
            statusText.Text = System.IO.File.Exists(filePath)
                ? "No tasks found. Last Sync: " + (lastSync ?? "--")
                : "team-tasks.md not found. Click Sync to fetch.";
            statusText.Foreground = subtext;
            return;
        }

        statusText.Text = $"Last Sync: {lastSync ?? "--"}";
        statusText.Foreground = subtext;

        foreach (var member in members)
        {
            var card = BuildMemberCard(member, text, subtext, surface1, surface2, redBrush, blueBrush);
            membersPanel.Children.Add(card);
        }
    }

    private static Border BuildMemberCard(
        TeamMemberCard member,
        Brush text,
        Brush subtext,
        Brush surface1,
        Brush surface2,
        Brush redBrush,
        Brush blueBrush)
    {
        var stack = new StackPanel();

        // ヘッダー
        var header = new TextBlock
        {
            Text = $"{member.MemberName} ({member.TaskCount})",
            Foreground = text,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(8, 7, 8, 4),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        stack.Children.Add(header);

        var separator = new Border
        {
            Background = surface2,
            Height = 1,
            Margin = new Thickness(4, 0, 4, 0)
        };
        stack.Children.Add(separator);

        // タスクを Asana プロジェクトごとにグループ化して表示
        var groups = member.Tasks
            .GroupBy(t => string.IsNullOrWhiteSpace(t.ProjectTag) ? "(no project)" : t.ProjectTag)
            .ToList();

        foreach (var group in groups)
        {
            // プロジェクトセクションヘッダー
            var sectionHeader = new TextBlock
            {
                Text = group.Key,
                Foreground = blueBrush,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(8, 6, 8, 2),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Opacity = 0.85
            };
            stack.Children.Add(sectionHeader);

            foreach (var task in group)
            {
                var taskRow = BuildTaskRow(task, text, subtext, redBrush, blueBrush);
                stack.Children.Add(taskRow);
            }
        }

        // 下部パディング
        stack.Children.Add(new Border { Height = 4 });

        return new Border
        {
            Background = surface1,
            BorderBrush = surface2,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(4),
            Width = 300,
            Child = stack
        };
    }

    private static FrameworkElement BuildTaskRow(
        TeamTaskItem task,
        Brush text,
        Brush subtext,
        Brush redBrush,
        Brush blueBrush)
    {
        var outerStack = new StackPanel { Margin = new Thickness(14, 1, 8, 3) };

        // タスク名
        var nameText = new TextBlock
        {
            Text = task.Name,
            Foreground = text,
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = task.Name
        };
        outerStack.Children.Add(nameText);

        // Due date + Asana リンク行
        var duePanelStack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 0) };

        var dueBrush = task.IsOverdue ? redBrush : subtext;
        var dueLabel = new TextBlock
        {
            Text = "Due: " + task.DueDisplay,
            Foreground = dueBrush,
            FontSize = 11
        };
        duePanelStack.Children.Add(dueLabel);

        if (task.IsOverdue)
        {
            duePanelStack.Children.Add(new TextBlock
            {
                Text = " ⚠",
                Foreground = redBrush,
                FontSize = 11
            });
        }

        if (!string.IsNullOrWhiteSpace(task.AsanaGid))
        {
            var asanaIcon = new Wpf.Ui.Controls.SymbolIcon
            {
                Symbol = Wpf.Ui.Controls.SymbolRegular.Open24,
                FontSize = 13,
                Foreground = blueBrush,
                Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = "Open in Asana"
            };
            asanaIcon.MouseLeftButtonUp += (_, _) =>
            {
                try { Process.Start(new ProcessStartInfo(task.AsanaUrl) { UseShellExecute = true }); }
                catch { }
            };
            duePanelStack.Children.Add(asanaIcon);
        }

        outerStack.Children.Add(duePanelStack);

        return outerStack;
    }
}
