using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using Button = System.Windows.Controls.Button;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using TextBox = System.Windows.Controls.TextBox;

namespace Curia.Views;

internal static class ImportPromptDialog
{
    /// <summary>
    /// ファイル選択後に補足プロンプト入力ダイアログを表示する。
    /// </summary>
    /// <returns>(ok: trueならImport続行, supplementaryPrompt: 入力テキスト)</returns>
    public static (bool ok, string supplementaryPrompt) Show(Window owner, string[] files)
    {
        var appResources = Application.Current.Resources;
        var surface  = (Brush)appResources["AppSurface0"];
        var surface1 = (Brush)appResources["AppSurface1"];
        var surface2 = (Brush)appResources["AppSurface2"];
        var text     = (Brush)appResources["AppText"];
        var subtext  = (Brush)appResources["AppSubtext0"];

        // ── タイトルバー ──────────────────────────────────────
        var titleBar = new Grid { Background = surface1, Height = 38 };
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleText = new TextBlock
        {
            Text = "Import Source",
            Foreground = text,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 0, 0)
        };
        Grid.SetColumn(titleText, 0);

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
        Grid.SetColumn(closeBtn, 1);

        titleBar.Children.Add(titleText);
        titleBar.Children.Add(closeBtn);

        // ── ファイル一覧 ───────────────────────────────────────
        var filesStack = new StackPanel { Margin = new Thickness(14, 10, 14, 0) };
        var filesLabel = new TextBlock
        {
            Text = files.Length == 1 ? "File to import:" : $"Files to import ({files.Length}):",
            Foreground = subtext,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 4)
        };
        filesStack.Children.Add(filesLabel);

        foreach (var f in files)
        {
            filesStack.Children.Add(new TextBlock
            {
                Text = "  " + System.IO.Path.GetFileName(f),
                Foreground = text,
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
        }

        // ── 補足プロンプト入力 ─────────────────────────────────
        var promptLabel = new TextBlock
        {
            Text = "Additional instructions (optional):",
            Foreground = subtext,
            FontSize = 12,
            Margin = new Thickness(14, 12, 14, 4)
        };

        var promptBox = new TextBox
        {
            AcceptsReturn = true,
            AcceptsTab = false,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 80,
            MaxHeight = 200,
            Margin = new Thickness(14, 0, 14, 0),
            Padding = new Thickness(8, 6, 8, 6),
            FontSize = 13,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = surface2,
            Foreground = text,
            BorderBrush = surface2,
            BorderThickness = new Thickness(1),
            CaretBrush = text
        };

        // ── ボタン行 ───────────────────────────────────────────
        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(14, 12, 14, 14)
        };

        var cancelBtn = new Button
        {
            Content = "Cancel",
            MinWidth = 80,
            Height = 32,
            Margin = new Thickness(0, 0, 8, 0),
            Background = Brushes.Transparent,
            Foreground = subtext,
            BorderThickness = new Thickness(1),
            IsCancel = true
        };

        var importBtn = new Wpf.Ui.Controls.Button
        {
            Content = "Import",
            Appearance = Wpf.Ui.Controls.ControlAppearance.Primary,
            MinWidth = 80,
            Height = 32,
            IsDefault = true
        };

        btnPanel.Children.Add(cancelBtn);
        btnPanel.Children.Add(importBtn);

        // ── レイアウト組み立て ─────────────────────────────────
        var root = new StackPanel { Background = surface };
        root.Children.Add(titleBar);
        root.Children.Add(filesStack);
        root.Children.Add(promptLabel);
        root.Children.Add(promptBox);
        root.Children.Add(btnPanel);

        bool confirmed = false;

        var dialog = new Window
        {
            Title = "",
            Owner = owner,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.None,
            SizeToContent = SizeToContent.Height,
            MinHeight = 0,
            Width = 500,
            Background = surface,
            Foreground = text,
            BorderBrush = surface2,
            BorderThickness = new Thickness(1),
            Content = root
        };

        importBtn.Click += (_, _) => { confirmed = true; dialog.Close(); };
        cancelBtn.Click += (_, _) => dialog.Close();
        closeBtn.Click  += (_, _) => dialog.Close();
        titleBar.MouseLeftButtonDown += (_, _) => dialog.DragMove();

        dialog.ShowDialog();

        return (confirmed, confirmed ? promptBox.Text?.Trim() ?? "" : "");
    }
}
