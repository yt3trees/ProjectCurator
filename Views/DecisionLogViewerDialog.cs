using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using ProjectCurator.Models;

using Button = System.Windows.Controls.Button;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Cursors = System.Windows.Input.Cursors;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace ProjectCurator.Views;

internal static class DecisionLogViewerDialog
{
    public static void ShowDialog(Window owner, string titleSuffix, List<DecisionLogItem> entries, Action<string> onOpenInEditor)
    {
        var appResources = Application.Current.Resources;
        var surface  = (Brush)appResources["AppSurface0"];
        var surface1 = (Brush)appResources["AppSurface1"];
        var surface2 = (Brush)appResources["AppSurface2"];
        var text     = (Brush)appResources["AppText"];
        var subtext  = (Brush)appResources["AppSubtext0"];

        var titleBar = new Grid { Background = surface1, Height = 38 };
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleIcon = new TextBlock
        {
            Text = "📝",
            FontSize = 13,
            Margin = new Thickness(12, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(titleIcon, 0);

        var titleText = new TextBlock
        {
            Text = $"Decision Logs - {titleSuffix}",
            Foreground = text,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(titleText, 1);

        var closeBtn = new Button
        {
            Content = "✕",
            Width = 34,
            Height = 26,
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = subtext,
            FontSize = 13
        };
        Grid.SetColumn(closeBtn, 2);
        
        titleBar.Children.Add(titleIcon);
        titleBar.Children.Add(titleText);
        titleBar.Children.Add(closeBtn);

        var headerGrid = new Grid
        {
            Margin = new Thickness(16, 12, 16, 4)
        };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var summaryText = new TextBlock
        {
            Text = entries.Count > 0 ? "Latest decisions (Date descending)" : "No decision logs found.",
            Foreground = subtext,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(summaryText, 0);
        headerGrid.Children.Add(summaryText);

        var filterBox = new Wpf.Ui.Controls.TextBox
        {
            PlaceholderText = "Filter by keyword...",
            Width = 200,
            Padding = new Thickness(8, 4, 8, 4),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(filterBox, 1);
        headerGrid.Children.Add(filterBox);

        var scrollView = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(16, 4, 16, 12)
        };

        var stackPanel = new StackPanel();
        var cardMap = new List<(Border Element, DecisionLogItem Item)>();

        // Need access to dialogWindow inside the click loop, so declare it first
        Window dialogWindow = null!;

        foreach (var entry in entries)
        {
            var cardGrid = new Grid
            {
                Margin = new Thickness(0, 0, 0, 8)
            };
            cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var cardBorder = new Border
            {
                Background = surface1,
                BorderBrush = surface2,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(12, 10, 12, 10),
                Child = cardGrid
            };

            var infoStack = new StackPanel();

            // Date / Status / Trigger Row
            var metaPanels = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            
            metaPanels.Children.Add(CreateBadge(entry.Date, appResources.Contains("AppBlue") ? (Brush)appResources["AppBlue"] : surface2));
            if (!string.IsNullOrWhiteSpace(entry.Status))
                metaPanels.Children.Add(CreateBadge(entry.Status, appResources.Contains("AppGreen") ? (Brush)appResources["AppGreen"] : surface2));
            if (!string.IsNullOrWhiteSpace(entry.Trigger))
                metaPanels.Children.Add(CreateBadge(entry.Trigger, appResources.Contains("AppMauve") ? (Brush)appResources["AppMauve"] : surface2));

            infoStack.Children.Add(metaPanels);

            var titleTextBlock = new TextBlock
            {
                Text = string.IsNullOrEmpty(entry.Title) ? "Untitled" : entry.Title,
                Foreground = text,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8),
                TextWrapping = TextWrapping.Wrap
            };
            infoStack.Children.Add(titleTextBlock);

            if (!string.IsNullOrWhiteSpace(entry.ChosenSummary))
            {
                infoStack.Children.Add(new TextBlock
                {
                    Text = "Chosen: " + entry.ChosenSummary,
                    Foreground = text,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 4)
                });
            }

            if (!string.IsNullOrWhiteSpace(entry.WhySummary))
            {
                infoStack.Children.Add(new TextBlock
                {
                    Text = "Why: " + entry.WhySummary,
                    Foreground = subtext,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap
                });
            }

            Grid.SetColumn(infoStack, 0);
            cardGrid.Children.Add(infoStack);

            var openBtn = new Wpf.Ui.Controls.Button
            {
                Content = "Open in Editor",
                Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary,
                Height = 28,
                Padding = new Thickness(12, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(12, 0, 0, 0),
                Cursor = Cursors.Hand
            };
            openBtn.Click += (_, _) =>
            {
                onOpenInEditor(entry.FilePath);
                dialogWindow?.Close();
            };
            Grid.SetColumn(openBtn, 1);
            cardGrid.Children.Add(openBtn);

            stackPanel.Children.Add(cardBorder);
            cardMap.Add((cardBorder, entry));
        }

        filterBox.TextChanged += (_, _) =>
        {
            var query = filterBox.Text?.Trim().ToLowerInvariant() ?? "";
            int visibleCount = 0;
            foreach (var map in cardMap)
            {
                bool match = string.IsNullOrWhiteSpace(query);
                if (!match)
                {
                    var item = map.Item;
                    match = (item.Title?.ToLowerInvariant().Contains(query) == true) ||
                            (item.Status?.ToLowerInvariant().Contains(query) == true) ||
                            (item.Trigger?.ToLowerInvariant().Contains(query) == true) ||
                            (item.ChosenSummary?.ToLowerInvariant().Contains(query) == true) ||
                            (item.WhySummary?.ToLowerInvariant().Contains(query) == true) ||
                            (item.Date?.ToLowerInvariant().Contains(query) == true);
                }
                
                map.Element.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
                if (match) visibleCount++;
            }
            
            summaryText.Text = string.IsNullOrWhiteSpace(query)
                ? (entries.Count > 0 ? "Latest decisions (Date descending)" : "No decision logs found.")
                : $"Showing {visibleCount} of {entries.Count} decisions matching filter";
        };

        scrollView.Content = stackPanel;

        var footerPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(16, 0, 16, 12)
        };
        var closeFooterBtn = new Wpf.Ui.Controls.Button
        {
            Content = "Close",
            Appearance = Wpf.Ui.Controls.ControlAppearance.Primary,
            MinWidth = 100,
            Height = 32,
            IsCancel = true
        };
        footerPanel.Children.Add(closeFooterBtn);

        var root = new Grid { Background = surface };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        
        Grid.SetRow(titleBar, 0);
        Grid.SetRow(headerGrid, 1);
        Grid.SetRow(scrollView, 2);
        Grid.SetRow(footerPanel, 3);
        
        root.Children.Add(titleBar);
        root.Children.Add(headerGrid);
        root.Children.Add(scrollView);
        root.Children.Add(footerPanel);

        dialogWindow = new Window
        {
            Title = "",
            Owner = owner,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize,
            WindowStyle = WindowStyle.None,
            MinWidth = 600,
            MinHeight = 400,
            Width = 720,
            Height = 560,
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

        closeBtn.Click += (_, _) => dialogWindow.Close();
        closeFooterBtn.Click += (_, _) => dialogWindow.Close();
        titleBar.MouseLeftButtonDown += (_, ev) =>
        {
            if (ev.LeftButton == MouseButtonState.Pressed) dialogWindow.DragMove();
        };

        dialogWindow.ShowDialog();
    }

    private static Border CreateBadge(string text, System.Windows.Media.Brush background)
    {
        return new Border
        {
            Background = new SolidColorBrush(((SolidColorBrush)background).Color) { Opacity = 0.2 },
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = text,
                Foreground = background,
                FontSize = 10,
                FontWeight = FontWeights.Bold
            }
        };
    }
}
