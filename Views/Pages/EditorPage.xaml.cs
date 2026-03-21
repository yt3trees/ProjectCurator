using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MediaColor = System.Windows.Media.Color;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Search;
using ICSharpCode.AvalonEdit.Highlighting;
using Wpf.Ui.Controls;
using ProjectCurator.ViewModels;
using WpfUserControl = System.Windows.Controls.UserControl;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace ProjectCurator.Views.Pages;

public partial class EditorPage : WpfUserControl, INavigableView<EditorViewModel>
{
    public EditorViewModel ViewModel { get; }

    private readonly TextEditor _editor;
    private IHighlightingDefinition? _markdownDefinition;

    public EditorPage(EditorViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;

        // AvalonEdit インスタンスを生成
        _editor = new TextEditor
        {
            FontFamily = new System.Windows.Media.FontFamily("Consolas, 'Courier New', monospace"),
            FontSize = 14,
            WordWrap = false,
            ShowLineNumbers = true,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        // テキスト変更イベント
        _editor.TextChanged += (s, e) =>
        {
            ViewModel.NotifyTextChanged(_editor.Text);
        };

        // Ctrl+Click でリンクをブラウザで開く
        _editor.TextArea.PreviewMouseDown += OnEditorMouseDown;
        _editor.PreviewMouseWheel += OnEditorPreviewMouseWheel;

        InitializeComponent();

        // ContentPresenter に AvalonEdit をバインド
        EditorHost.Content = _editor;

        // 右クリックコンテキストメニュー (Work Folder パス挿入)
        var contextMenu = new ContextMenu();
        contextMenu.Opened += OnEditorContextMenuOpened;
        _editor.ContextMenu = contextMenu;

        // decision_log 新規入力ダイアログ
        ViewModel.RequestNewDecisionLogName = ShowNewDecisionLogDialog;

        // キーバインド
        KeyDown += OnPageKeyDown;

        // CurrentFile が変わったら必ずエディタ同期 + ハイライト適用
        // (OpenFileAndSelectNodeAsync 経由で SelectedItemChanged が発火しないケースをカバー)
        ViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(EditorViewModel.CurrentFile)
                && !string.IsNullOrEmpty(ViewModel.CurrentFile))
            {
                _editor.Text = ViewModel.EditorText;
                ApplyHighlighting(ViewModel.CurrentFile);
            }
        };
    }

    // -------------------------------------------------------------------------
    // ページロード
    // -------------------------------------------------------------------------
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // ハイライト定義を一度だけ登録
        RegisterMarkdownHighlighting();
        
        await ViewModel.LoadProjectsAsync();
        ApplyEditorTheme();
    }

    private void ApplyEditorTheme()
    {
        try
        {
            var bg = Application.Current.Resources["EditorBackground"] as System.Windows.Media.SolidColorBrush;
            var fg = Application.Current.Resources["EditorForeground"] as System.Windows.Media.SolidColorBrush;

            if (bg == null)
            {
                bg = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#0d1117"));
                fg = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#c9d1d9"));
            }
            
            _editor.Background = bg;
            _editor.Foreground = fg;
            _editor.LineNumbersForeground = (Application.Current.Resources["AppSubtext0"] as System.Windows.Media.SolidColorBrush)
                ?? new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#8b949e"));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[EditorPage] テーマ適用エラー: {ex.Message}");
        }
    }

    // -------------------------------------------------------------------------
    // Markdown ハイライト定義の登録
    // -------------------------------------------------------------------------
    private void RegisterMarkdownHighlighting()
    {
        if (_markdownDefinition != null) return;
        try
        {
            static HighlightingColor Clr(byte r, byte g, byte b) =>
                new() { Foreground = new SimpleHighlightingBrush(MediaColor.FromRgb(r, g, b)) };
            static HighlightingColor ClrBold(byte r, byte g, byte b) =>
                new() { Foreground = new SimpleHighlightingBrush(MediaColor.FromRgb(r, g, b)), FontWeight = FontWeights.Bold };
            static HighlightingColor ClrItalic(byte r, byte g, byte b) =>
                new() { Foreground = new SimpleHighlightingBrush(MediaColor.FromRgb(r, g, b)), FontStyle = FontStyles.Italic };
            static HighlightingColor ClrBoldItalic(byte r, byte g, byte b) =>
                new() { Foreground = new SimpleHighlightingBrush(MediaColor.FromRgb(r, g, b)), FontWeight = FontWeights.Bold, FontStyle = FontStyles.Italic };

            var heading    = ClrBold   (0x58, 0xa6, 0xff);
            var code       = Clr       (0xff, 0xa6, 0x57);
            var emphasis   = ClrItalic (0xff, 0xa8, 0xcc);
            var strong     = ClrBold   (0xff, 0xa8, 0xcc);
            var boldItalic = ClrBoldItalic(0xff, 0xa8, 0xcc);
            var blockquote = Clr       (0x7e, 0xe7, 0x87);
            var link       = Clr       (0x79, 0xc0, 0xff);
            var list       = Clr       (0xf0, 0x88, 0x3e);
            var comment    = Clr       (0x8b, 0x94, 0x9e);

            var rs = new HighlightingRuleSet();

            // Headings
            rs.Rules.Add(new HighlightingRule { Color = heading,    Regex = new Regex(@"^\#{1,6}[^\n]*",   RegexOptions.Multiline) });
            // Block quote
            rs.Rules.Add(new HighlightingRule { Color = blockquote, Regex = new Regex(@"^\s*>.*",          RegexOptions.Multiline) });
            // Horizontal rule
            rs.Rules.Add(new HighlightingRule { Color = new HighlightingColor { Foreground = new SimpleHighlightingBrush(MediaColor.FromRgb(0x48, 0x4f, 0x58)) },
                                                Regex = new Regex(@"^(\-{3,}|\*{3,}|_{3,})\s*$", RegexOptions.Multiline) });
            // Image (before link)
            rs.Rules.Add(new HighlightingRule { Color = link, Regex = new Regex(@"!\[.*?\]\([^\)]*\)") });
            // Link
            rs.Rules.Add(new HighlightingRule { Color = link, Regex = new Regex(@"\[.*?\]\([^\)]*\)") });
            // Unordered list
            rs.Rules.Add(new HighlightingRule { Color = list, Regex = new Regex(@"^\s*[\*\+\-]\s", RegexOptions.Multiline) });
            // Ordered list
            rs.Rules.Add(new HighlightingRule { Color = list, Regex = new Regex(@"^\s*\d+\.\s",   RegexOptions.Multiline) });

            // HTML comment: <!-- ... -->
            rs.Spans.Add(new HighlightingSpan { StartColor = comment, SpanColor = comment, EndColor = comment,
                StartExpression = new Regex(@"<!--"), EndExpression = new Regex(@"-->") });

            // Fenced code block (before inline code)
            rs.Spans.Add(new HighlightingSpan { StartColor = code, SpanColor = code, EndColor = code,
                StartExpression = new Regex("```"), EndExpression = new Regex("```") });
            // Inline code
            rs.Spans.Add(new HighlightingSpan { StartColor = code, SpanColor = code, EndColor = code,
                StartExpression = new Regex("`"), EndExpression = new Regex("`") });
            // Bold italic: ***
            rs.Spans.Add(new HighlightingSpan { StartColor = boldItalic, SpanColor = boldItalic, EndColor = boldItalic,
                StartExpression = new Regex(@"\*\*\*"), EndExpression = new Regex(@"\*\*\*") });
            // Bold: **
            rs.Spans.Add(new HighlightingSpan { StartColor = strong, SpanColor = strong, EndColor = strong,
                StartExpression = new Regex(@"\*\*"), EndExpression = new Regex(@"\*\*") });
            // Italic: *
            rs.Spans.Add(new HighlightingSpan { StartColor = emphasis, SpanColor = emphasis, EndColor = emphasis,
                StartExpression = new Regex(@"\*"), EndExpression = new Regex(@"\*") });

            _markdownDefinition = new MarkdownHighlightingDefinition(rs);
            System.Diagnostics.Debug.WriteLine("[EditorPage] Markdown ハイライト定義作成成功");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[EditorPage] Markdown ハイライト定義エラー: {ex.Message}");
        }
    }

    private void ApplyHighlighting(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLower();
        if (ext is ".md" or ".markdown")
        {
            if (_markdownDefinition == null)
                RegisterMarkdownHighlighting();
            _editor.SyntaxHighlighting = _markdownDefinition;
        }
        else
        {
            _editor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinitionByExtension(ext);
        }
    }

    private sealed class MarkdownHighlightingDefinition : IHighlightingDefinition
    {
        private readonly HighlightingRuleSet _mainRuleSet;
        public MarkdownHighlightingDefinition(HighlightingRuleSet mainRuleSet) => _mainRuleSet = mainRuleSet;
        public string Name => "Markdown";
        public HighlightingRuleSet MainRuleSet => _mainRuleSet;
        public IEnumerable<HighlightingColor> NamedHighlightingColors => [];
        public IDictionary<string, string> Properties { get; } = new Dictionary<string, string>();
        public HighlightingColor GetNamedColor(string name) => null!;
        public HighlightingRuleSet GetNamedRuleSet(string name) => null!;
    }

    // -------------------------------------------------------------------------
    // イベントハンドラ
    // -------------------------------------------------------------------------
    private async void OnProjectSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        await Task.CompletedTask;
    }

    private async void OnTreeSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is FileTreeNode node && !node.IsDirectory)
        {
            ViewModel.SelectedNode = node;
            await ViewModel.OpenFileAsync(node.FullPath);
            // CurrentFile PropertyChanged がエディタ同期とハイライト適用を担う
        }
    }

    private async void OnPageKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.S:
                    e.Handled = true;
                    ViewModel.NotifyTextChanged(_editor.Text);
                    await ViewModel.SaveAsync();
                    break;
                case Key.F:
                    e.Handled = true;
                    ViewModel.IsSearchBarVisible = true;
                    SearchTextBox.Focus();
                    break;
            }
        }
        else if (e.Key == Key.F3)
        {
            e.Handled = true;
            FindText(forward: Keyboard.Modifiers != ModifierKeys.Shift);
        }
        else if (e.Key == Key.Escape && ViewModel.IsSearchBarVisible)
        {
            ViewModel.IsSearchBarVisible = false;
            _editor.Focus();
            e.Handled = true;
        }
    }

    // -------------------------------------------------------------------------
    // 検索・リンク・ダイアログ
    // -------------------------------------------------------------------------
    private void OnSearchKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.Enter) { FindText(forward: Keyboard.Modifiers != ModifierKeys.Shift); e.Handled = true; }
        else if (e.Key == Key.Escape) { ViewModel.IsSearchBarVisible = false; _editor.Focus(); e.Handled = true; }
    }

    private void OnSearchNext(object sender, RoutedEventArgs e) => FindText(true);
    private void OnSearchPrev(object sender, RoutedEventArgs e) => FindText(false);
    private void OnSearchClose(object sender, RoutedEventArgs e) { ViewModel.IsSearchBarVisible = false; _editor.Focus(); }

    private void FindText(bool forward)
    {
        var query = ViewModel.SearchText;
        if (string.IsNullOrEmpty(query)) return;
        var text = _editor.Text;
        var start = _editor.CaretOffset;
        int idx = forward 
            ? text.IndexOf(query, Math.Min(text.Length, start + 1), StringComparison.OrdinalIgnoreCase) 
            : text.LastIndexOf(query, Math.Max(0, start - 1), StringComparison.OrdinalIgnoreCase);
        
        if (idx < 0) idx = forward ? text.IndexOf(query, 0, StringComparison.OrdinalIgnoreCase) : text.LastIndexOf(query, text.Length, StringComparison.OrdinalIgnoreCase);
        
        if (idx >= 0)
        {
            _editor.Select(idx, query.Length);
            _editor.CaretOffset = idx;
            _editor.ScrollToLine(_editor.Document.GetLineByOffset(idx).LineNumber);
        }
        _editor.Focus();
    }

    // -------------------------------------------------------------------------
    // 右クリックコンテキストメニュー (Work Folder パス挿入)
    // -------------------------------------------------------------------------
    private async void OnEditorContextMenuOpened(object sender, RoutedEventArgs e)
    {
        var menu = (ContextMenu)sender;
        menu.Items.Clear();

        var folders = await Task.Run(() => FindWorkFolders());
        if (folders.Count == 0)
        {
            menu.Items.Add(new System.Windows.Controls.MenuItem { Header = "No work folders found", IsEnabled = false });
            return;
        }

        var header = new System.Windows.Controls.MenuItem { Header = "Insert Work Folder Path", IsEnabled = false };
        menu.Items.Add(header);
        menu.Items.Add(new Separator());

        foreach (var folder in folders)
        {
            var item = new System.Windows.Controls.MenuItem { Header = Path.GetFileName(folder) };
            var captured = folder;
            item.Click += (s, args) => InsertWorkFolderLink(captured);
            menu.Items.Add(item);
        }
    }

    private List<string> FindWorkFolders()
    {
        var startDir = string.IsNullOrEmpty(ViewModel.CurrentFile)
            ? null
            : Path.GetDirectoryName(ViewModel.CurrentFile);

        if (startDir == null) return [];

        string? projectRoot = null;
        var current = new DirectoryInfo(startDir);
        while (current != null)
        {
            var sharedWork = Path.Combine(current.FullName, "shared", "_work");
            if (Directory.Exists(sharedWork)) { projectRoot = current.FullName; break; }
            current = current.Parent;
        }

        if (projectRoot == null) return [];

        var workRoot = Path.Combine(projectRoot, "shared", "_work");
        var pattern = new Regex(@"^\d{8}_");

        var result = Directory
            .EnumerateDirectories(workRoot, "*", SearchOption.AllDirectories)
            .Where(d => pattern.IsMatch(Path.GetFileName(d)))
            .OrderByDescending(d => Path.GetFileName(d))
            .Take(30)
            .ToList();

        return result;
    }

    private void InsertWorkFolderLink(string folderPath)
    {
        var name = Path.GetFileName(folderPath);
        var link = $"[{name}]({folderPath})";
        _editor.Document.Insert(_editor.CaretOffset, link);
        _editor.Focus();
    }

    private void OnEditorMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control || e.ChangedButton != MouseButton.Left) return;
        var pos = _editor.GetPositionFromPoint(e.GetPosition(_editor));
        if (pos == null) return;
        var offset = _editor.Document.GetOffset(pos.Value.Line, pos.Value.Column);
        var line = _editor.Document.GetLineByOffset(offset);
        var lineText = _editor.Document.GetText(line.Offset, line.Length);

        var urlMatch = System.Text.RegularExpressions.Regex.Match(lineText, @"\[.*?\]\((https?://[^\)]+)\)");
        if (urlMatch.Success) { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(urlMatch.Groups[1].Value) { UseShellExecute = true }); e.Handled = true; return; }

        var pathMatch = System.Text.RegularExpressions.Regex.Match(lineText, @"\[.*?\]\((?!https?://)([^\)]+)\)");
        if (pathMatch.Success)
        {
            var rel = pathMatch.Groups[1].Value;
            string absPath = Path.IsPathRooted(rel) ? rel : (!string.IsNullOrEmpty(ViewModel.CurrentFile) ? Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ViewModel.CurrentFile)!, rel)) : "");
            if (string.IsNullOrEmpty(absPath)) return;
            if (Directory.Exists(absPath)) { System.Diagnostics.Process.Start("explorer.exe", absPath); e.Handled = true; }
            else if (File.Exists(absPath)) { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(absPath) { UseShellExecute = true }); e.Handled = true; }
        }
    }

    private void OnEditorPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return;

        var scrollViewer = FindVisualChild<ScrollViewer>(_editor);
        if (scrollViewer == null || scrollViewer.ScrollableWidth <= 0) return;

        const double horizontalStep = 48d;
        var direction = e.Delta > 0 ? -1d : 1d;
        scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset + (direction * horizontalStep));
        e.Handled = true;
    }

    private static TChild? FindVisualChild<TChild>(DependencyObject parent)
        where TChild : DependencyObject
    {
        var childCount = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < childCount; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is TChild typedChild)
            {
                return typedChild;
            }

            var nestedChild = FindVisualChild<TChild>(child);
            if (nestedChild != null)
            {
                return nestedChild;
            }
        }

        return null;
    }

    private void OnDeleteDecisionLog(object sender, RoutedEventArgs e)
    {
        if (FileTreeView.SelectedItem is FileTreeNode node) ViewModel.DeleteDecisionLogCommand.Execute(node);
    }

    private Task<string?> ShowNewDecisionLogDialog()
    {
        var dialog = new InputDialog("New Decision Log", "File name (date is added automatically):") { Owner = Window.GetWindow(this) };
        return Task.FromResult(dialog.ShowDialog() == true ? dialog.InputText : null);
    }
}

internal class InputDialog : Window
{
    private readonly System.Windows.Controls.TextBox _textBox;
    public string InputText => _textBox.Text;
    public InputDialog(string title, string prompt)
    {
        Title = title;
        Width = 440;
        Height = 190;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        var appResources = Application.Current.Resources;
        var appSurface0 = appResources["AppSurface0"] as System.Windows.Media.Brush;
        var appSurface1 = appResources["AppSurface1"] as System.Windows.Media.Brush;
        var appSurface2 = appResources["AppSurface2"] as System.Windows.Media.Brush;
        var appText = appResources["AppText"] as System.Windows.Media.Brush;
        var appSubtext0 = appResources["AppSubtext0"] as System.Windows.Media.Brush;

        var fallbackSurface0 = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#111827"));
        var fallbackSurface1 = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1f2937"));
        var fallbackSurface2 = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#374151"));
        var fallbackText = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#e5e7eb"));
        var fallbackSubtext = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#cbd5e1"));

        Background = appSurface0 ?? fallbackSurface0;
        Foreground = appText ?? fallbackText;

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = prompt,
            Margin = new Thickness(0, 0, 0, 8),
            Foreground = appText ?? fallbackText
        });

        _textBox = new System.Windows.Controls.TextBox
        {
            Margin = new Thickness(0, 0, 0, 12),
            Background = appSurface1 ?? fallbackSurface1,
            Foreground = appText ?? fallbackText,
            BorderBrush = appSurface2 ?? fallbackSurface2,
            CaretBrush = appText ?? fallbackText,
            Padding = new Thickness(8, 6, 8, 6)
        };
        _textBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) DialogResult = true; else if (e.Key == Key.Escape) DialogResult = false; };
        panel.Children.Add(_textBox);

        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        var ok = new System.Windows.Controls.Button
        {
            Content = "Create",
            Width = 88,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true,
            Background = appSurface2 ?? fallbackSurface2,
            Foreground = appText ?? fallbackText,
            BorderBrush = appSurface2 ?? fallbackSurface2
        };
        ok.Click += (s, e) => DialogResult = true;

        var cancel = new System.Windows.Controls.Button
        {
            Content = "Cancel",
            Width = 88,
            IsCancel = true,
            Background = appSurface1 ?? fallbackSurface1,
            Foreground = appSubtext0 ?? fallbackSubtext,
            BorderBrush = appSurface2 ?? fallbackSurface2
        };
        cancel.Click += (s, e) => DialogResult = false;

        btnPanel.Children.Add(ok);
        btnPanel.Children.Add(cancel);
        panel.Children.Add(btnPanel);
        Content = panel;
        Loaded += (s, e) => _textBox.Focus();
    }
}
