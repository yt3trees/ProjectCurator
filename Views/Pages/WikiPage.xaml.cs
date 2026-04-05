using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Xml;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using System.Text.RegularExpressions;
using Wpf.Ui.Controls;
using Curia.Services;
using Curia.ViewModels;
using WpfUserControl = System.Windows.Controls.UserControl;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfKey = System.Windows.Input.Key;
using WpfKeyboard = System.Windows.Input.Keyboard;
using WpfModifierKeys = System.Windows.Input.ModifierKeys;
using WpfDragEventArgs = System.Windows.DragEventArgs;
using WpfDragDropEffects = System.Windows.DragDropEffects;
using WpfDataFormats = System.Windows.DataFormats;
using Win32OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using MediaColor = System.Windows.Media.Color;

namespace Curia.Views.Pages;

public partial class WikiPage : WpfUserControl, INavigableView<WikiViewModel>
{
    public WikiViewModel ViewModel { get; }

    private bool _isInitialized;
    private System.Windows.Media.Brush? _tabActiveBrush;
    private System.Windows.Media.Brush? _tabInactiveBrush;
    private readonly TextEditor _wikiEditor;
    private readonly FlowDocumentScrollViewer _wikiRenderViewer;
    private bool _syncingEditor;
    private IHighlightingDefinition? _markdownHighlighting;
    private string? _lastRenderedPath;

    public WikiPage(WikiViewModel viewModel)
    {
        ViewModel = viewModel;
        ViewModel.OnOpenInEditor = (project, filePath) =>
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.NavigateToEditorAndOpenFile(project, filePath);
        };
        DataContext = ViewModel;
        InitializeComponent();

        _wikiEditor = new TextEditor
        {
            FontFamily = new System.Windows.Media.FontFamily("Consolas, 'Courier New', monospace"),
            FontSize = 13,
            WordWrap = true,
            ShowLineNumbers = true,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        AddBottomViewportPadding(_wikiEditor);
        _wikiEditor.TextChanged += (_, _) =>
        {
            if (_syncingEditor) return;
            ViewModel.PreviewContent = _wikiEditor.Text;
        };
        _wikiEditor.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == WpfKey.S && WpfKeyboard.Modifiers.HasFlag(WpfModifierKeys.Control))
            {
                ViewModel.SaveCurrentPageCommand.Execute(null);
                e.Handled = true;
            }
        };
        _wikiRenderViewer = new FlowDocumentScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            IsToolBarVisible = false,
            Document = new FlowDocument()
        };
        // スクロール速度の改善 (0.5倍に微調整：ちょうど1行ずつ追える心地よい速度)
        _wikiRenderViewer.PreviewMouseWheel += (s, e) =>
        {
            var sv = FindVisualChild<ScrollViewer>(_wikiRenderViewer);
            if (sv != null)
            {
                sv.ScrollToVerticalOffset(sv.VerticalOffset - (e.Delta * 0.5));
                e.Handled = true;
            }
        };
        WikiEditorHost.Content = _wikiEditor;
        WikiRenderHost.Content = _wikiRenderViewer;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_isInitialized) return;
        _isInitialized = true;

        var appRes = Application.Current.Resources;
        _tabActiveBrush   = appRes.Contains("AppSurface2")
            ? (System.Windows.Media.Brush)appRes["AppSurface2"]
            : System.Windows.Media.Brushes.Gray;
        _tabInactiveBrush = System.Windows.Media.Brushes.Transparent;
        UpdateTabStyles();
        ApplyEditorTheme();
        RegisterMarkdownHighlighting();
        _wikiEditor.SyntaxHighlighting = _markdownHighlighting;

        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(WikiViewModel.ActiveTab))
                UpdateTabStyles();
            else if (args.PropertyName == nameof(WikiViewModel.PreviewContent))
            {
                SyncEditorFromViewModel();
                SyncRenderFromViewModel();
            }
        };

        await ViewModel.InitAsync();
    }

    private void ResetRenderScroll()
    {
        // UIスレッドのレイアウト更新が完全に終わるのを待ってからリセット
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            var sv = FindVisualChild<ScrollViewer>(_wikiRenderViewer);
            sv?.ScrollToHome();
        });
    }

    private static T? FindVisualChild<T>(DependencyObject obj) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
        {
            var child = VisualTreeHelper.GetChild(obj, i);
            if (child is T t) return t;
            var childOfChild = FindVisualChild<T>(child);
            if (childOfChild != null) return childOfChild;
        }
        return null;
    }

    private void SyncEditorFromViewModel()
    {
        var text = ViewModel.PreviewContent ?? "";
        if (string.Equals(_wikiEditor.Text, text, StringComparison.Ordinal))
            return;

        _syncingEditor = true;
        _wikiEditor.Text = text;
        _syncingEditor = false;
    }

    private void SyncRenderFromViewModel()
    {
        _wikiRenderViewer.Document = BuildMarkdownDocument(ViewModel.PreviewContent ?? "");
        
        // ファイルが切り替わった時のみスクロールをリセット（編集中に上に戻るのを防ぐ）
        if (_lastRenderedPath != ViewModel.SelectedPagePath)
        {
            _lastRenderedPath = ViewModel.SelectedPagePath;
            ResetRenderScroll();
        }
    }

    private void ApplyEditorTheme()
    {
        var resources = Application.Current.Resources;
        _wikiEditor.Background = resources["EditorBackground"] as System.Windows.Media.Brush
            ?? resources["AppSurface0"] as System.Windows.Media.Brush
            ?? System.Windows.Media.Brushes.Black;
        _wikiEditor.Foreground = resources["EditorForeground"] as System.Windows.Media.Brush
            ?? resources["AppText"] as System.Windows.Media.Brush
            ?? System.Windows.Media.Brushes.White;
        _wikiEditor.LineNumbersForeground = resources["AppSubtext0"] as System.Windows.Media.Brush
            ?? System.Windows.Media.Brushes.Gray;
    }

    private static void AddBottomViewportPadding(TextEditor editor)
    {
        editor.Options.AllowScrollBelowDocument = false;
        var margin = editor.TextArea.TextView.Margin;
        editor.TextArea.TextView.Margin = new Thickness(
            margin.Left,
            margin.Top,
            margin.Right,
            SystemParameters.HorizontalScrollBarHeight + 2);
    }

    private static FlowDocument BuildMarkdownDocument(string markdown)
    {
        var doc = new FlowDocument
        {
            PagePadding = new Thickness(14, 10, 14, SystemParameters.HorizontalScrollBarHeight + 6),
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI, Lucida Sans Unicode, Arial"),
            FontSize = 13,
            LineHeight = 20 // 行間を少し広げて読みやすく
        };

        // Set default foreground from theme
        if (Application.Current?.Resources["AppText"] is System.Windows.Media.Brush appText)
        {
            doc.Foreground = appText;
        }

        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var inCode = false;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i] ?? string.Empty;
            var trimmed = line.Trim();

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                inCode = !inCode;
                continue;
            }

            if (inCode)
            {
                var p = new Paragraph(new Run(line))
                {
                    FontFamily = new System.Windows.Media.FontFamily("Consolas, 'Courier New', monospace"),
                    FontSize = 12, // コードブロックは少し小さめが綺麗
                    Background = new SolidColorBrush(MediaColor.FromRgb(0x1f, 0x24, 0x2d)),
                    Foreground = System.Windows.Media.Brushes.Gainsboro,
                    Margin = new Thickness(0, 0, 0, 2),
                    Padding = new Thickness(6, 2, 6, 2)
                };
                doc.Blocks.Add(p);
                continue;
            }

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                doc.Blocks.Add(new Paragraph { Margin = new Thickness(0, 0, 0, 4) });
                continue;
            }

            // Table support
            if (trimmed.StartsWith("|", StringComparison.Ordinal) && i + 1 < lines.Length)
            {
                var nextLine = lines[i + 1]?.Trim() ?? "";
                if (nextLine.StartsWith("|", StringComparison.Ordinal) && nextLine.Contains("---"))
                {
                    var table = ParseTable(lines, ref i);
                    if (table != null)
                    {
                        doc.Blocks.Add(table);
                        continue;
                    }
                }
            }

            if (trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                var level = Math.Min(6, trimmed.TakeWhile(c => c == '#').Count());
                var text = trimmed[level..].Trim();
                // 見出しサイズも全体に合わせて少しスケールダウン
                var size = level switch { 1 => 20d, 2 => 18d, 3 => 16d, 4 => 14d, _ => 13d };
                doc.Blocks.Add(new Paragraph(new Run(text))
                {
                    FontWeight = FontWeights.SemiBold,
                    FontSize = size,
                    Foreground = new SolidColorBrush(MediaColor.FromRgb(0x79, 0xc0, 0xff)),
                    Margin = new Thickness(0, 8, 0, 4)
                });
                continue;
            }

            if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal))
            {
                doc.Blocks.Add(new Paragraph(new Run("• " + trimmed[2..].Trim()))
                {
                    Margin = new Thickness(10, 0, 0, 2)
                });
                continue;
            }

            var ordered = ParseOrderedListLine(trimmed);
            if (ordered != null)
            {
                doc.Blocks.Add(new Paragraph(new Run(ordered))
                {
                    Margin = new Thickness(10, 0, 0, 2)
                });
                continue;
            }

            if (trimmed.StartsWith("> ", StringComparison.Ordinal))
            {
                doc.Blocks.Add(new Paragraph(new Run(trimmed[2..]))
                {
                    Margin = new Thickness(12, 0, 0, 4),
                    Foreground = new SolidColorBrush(MediaColor.FromRgb(0x9b, 0xd0, 0x9b))
                });
                continue;
            }

            doc.Blocks.Add(new Paragraph(new Run(line))
            {
                Margin = new Thickness(0, 0, 0, 4)
            });
        }

        return doc;
    }

    private static Table? ParseTable(string[] lines, ref int i)
    {
        var headerLine = lines[i];
        var headerCells = ExtractCells(headerLine);
        if (headerCells.Length == 0) return null;

        var table = new Table
        {
            CellSpacing = 0,
            BorderBrush = Application.Current?.Resources["AppSurface2"] as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Gray,
            BorderThickness = new Thickness(0, 1, 0, 1),
            Margin = new Thickness(0, 8, 0, 12)
        };

        for (int c = 0; c < headerCells.Length; c++)
        {
            table.Columns.Add(new TableColumn());
        }

        var headerRowGroup = new TableRowGroup();
        var headerRow = new TableRow();
        foreach (var cell in headerCells)
        {
            var p = new Paragraph(new Run(cell))
            {
                FontWeight = FontWeights.SemiBold,
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0)
            };
            var tableCell = new TableCell(p)
            {
                Background = new SolidColorBrush(MediaColor.FromRgb(0x1f, 0x24, 0x2d)),
                Foreground = System.Windows.Media.Brushes.Gainsboro,
                BorderBrush = table.BorderBrush,
                BorderThickness = new Thickness(0, 0, 0, 1)
            };
            headerRow.Cells.Add(tableCell);
        }
        headerRowGroup.Rows.Add(headerRow);
        table.RowGroups.Add(headerRowGroup);

        var bodyRowGroup = new TableRowGroup();
        i += 2; // skip header and separator

        while (i < lines.Length)
        {
            var line = lines[i];
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("|", StringComparison.Ordinal)) break;

            var cells = ExtractCells(line);
            var row = new TableRow();
            for (int c = 0; c < headerCells.Length; c++)
            {
                var cellText = c < cells.Length ? cells[c] : "";
                var p = new Paragraph(new Run(cellText))
                {
                    Padding = new Thickness(8, 4, 8, 4),
                    Margin = new Thickness(0)
                };
                row.Cells.Add(new TableCell(p)
                {
                    BorderBrush = table.BorderBrush,
                    BorderThickness = new Thickness(0, 0, 0, 1)
                });
            }
            bodyRowGroup.Rows.Add(row);
            i++;
        }
        i--; // back up one so the outer loop points to the last table line, then increments

        table.RowGroups.Add(bodyRowGroup);
        return table;
    }

    private static string[] ExtractCells(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.StartsWith("|", StringComparison.Ordinal)) trimmed = trimmed[1..];
        if (trimmed.EndsWith("|", StringComparison.Ordinal)) trimmed = trimmed[..^1];
        return trimmed.Split('|').Select(c => c.Trim()).ToArray();
    }

    private static string? ParseOrderedListLine(string trimmed)
    {
        if (trimmed.Length < 3 || !char.IsDigit(trimmed[0])) return null;
        var idx = 0;
        while (idx < trimmed.Length && char.IsDigit(trimmed[idx])) idx++;
        if (idx + 1 >= trimmed.Length || trimmed[idx] != '.' || trimmed[idx + 1] != ' ') return null;
        return trimmed;
    }

    private void RegisterMarkdownHighlighting()
    {
        if (_markdownHighlighting != null) return;

        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var resourceName = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("Markdown.xshd", StringComparison.OrdinalIgnoreCase));
            if (resourceName == null) return;

            using var stream = asm.GetManifestResourceStream(resourceName);
            if (stream == null) return;
            using var reader = XmlReader.Create(stream);
            _markdownHighlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
            HighlightingManager.Instance.RegisterHighlighting("Markdown", new[] { ".md", ".markdown" }, _markdownHighlighting);
        }
        catch
        {
            _markdownHighlighting = BuildFallbackMarkdownHighlighting();
        }

        _markdownHighlighting ??= BuildFallbackMarkdownHighlighting();
    }

    private static IHighlightingDefinition BuildFallbackMarkdownHighlighting()
    {
        static HighlightingColor Clr(byte r, byte g, byte b) =>
            new() { Foreground = new SimpleHighlightingBrush(MediaColor.FromRgb(r, g, b)) };
        static HighlightingColor ClrBold(byte r, byte g, byte b) =>
            new() { Foreground = new SimpleHighlightingBrush(MediaColor.FromRgb(r, g, b)), FontWeight = FontWeights.Bold };
        static HighlightingColor ClrItalic(byte r, byte g, byte b) =>
            new() { Foreground = new SimpleHighlightingBrush(MediaColor.FromRgb(r, g, b)), FontStyle = FontStyles.Italic };
        static HighlightingColor ClrBoldItalic(byte r, byte g, byte b) =>
            new() { Foreground = new SimpleHighlightingBrush(MediaColor.FromRgb(r, g, b)), FontWeight = FontWeights.Bold, FontStyle = FontStyles.Italic };

        var heading    = ClrBold(0x58, 0xa6, 0xff);
        var code       = Clr(0xff, 0xa6, 0x57);
        var emphasis   = ClrItalic(0xff, 0xa8, 0xcc);
        var strong     = ClrBold(0xff, 0xa8, 0xcc);
        var boldItalic = ClrBoldItalic(0xff, 0xa8, 0xcc);
        var blockquote = Clr(0x7e, 0xe7, 0x87);
        var link       = Clr(0x79, 0xc0, 0xff);
        var list       = Clr(0xf0, 0x88, 0x3e);
        var comment    = Clr(0x8b, 0x94, 0x9e);

        var rs = new HighlightingRuleSet();
        rs.Rules.Add(new HighlightingRule { Color = heading, Regex = new Regex(@"^\#{1,6}[^\n]*", RegexOptions.Multiline) });
        rs.Rules.Add(new HighlightingRule { Color = blockquote, Regex = new Regex(@"^\s*>.*", RegexOptions.Multiline) });
        rs.Rules.Add(new HighlightingRule
        {
            Color = new HighlightingColor { Foreground = new SimpleHighlightingBrush(MediaColor.FromRgb(0x48, 0x4f, 0x58)) },
            Regex = new Regex(@"^(\-{3,}|\*{3,}|_{3,})\s*$", RegexOptions.Multiline)
        });
        rs.Rules.Add(new HighlightingRule { Color = link, Regex = new Regex(@"!\[.*?\]\([^\)]*\)") });
        rs.Rules.Add(new HighlightingRule { Color = link, Regex = new Regex(@"\[.*?\]\([^\)]*\)") });
        rs.Rules.Add(new HighlightingRule { Color = list, Regex = new Regex(@"^\s*[\*\+\-]\s", RegexOptions.Multiline) });
        rs.Rules.Add(new HighlightingRule { Color = list, Regex = new Regex(@"^\s*\d+\.\s", RegexOptions.Multiline) });

        rs.Spans.Add(new HighlightingSpan { StartColor = comment, SpanColor = comment, EndColor = comment, StartExpression = new Regex(@"<!--"), EndExpression = new Regex(@"-->") });
        rs.Spans.Add(new HighlightingSpan { StartColor = code, SpanColor = code, EndColor = code, StartExpression = new Regex("```"), EndExpression = new Regex("```") });
        rs.Spans.Add(new HighlightingSpan { StartColor = code, SpanColor = code, EndColor = code, StartExpression = new Regex("`"), EndExpression = new Regex("`") });
        rs.Spans.Add(new HighlightingSpan { StartColor = boldItalic, SpanColor = boldItalic, EndColor = boldItalic, StartExpression = new Regex(@"\*\*\*"), EndExpression = new Regex(@"\*\*\*") });
        rs.Spans.Add(new HighlightingSpan { StartColor = strong, SpanColor = strong, EndColor = strong, StartExpression = new Regex(@"\*\*"), EndExpression = new Regex(@"\*\*") });
        rs.Spans.Add(new HighlightingSpan { StartColor = emphasis, SpanColor = emphasis, EndColor = emphasis, StartExpression = new Regex(@"\*"), EndExpression = new Regex(@"\*") });

        return new WikiMarkdownHighlightingDefinition(rs);
    }

    private sealed class WikiMarkdownHighlightingDefinition : IHighlightingDefinition
    {
        private readonly HighlightingRuleSet _mainRuleSet;
        public WikiMarkdownHighlightingDefinition(HighlightingRuleSet mainRuleSet) => _mainRuleSet = mainRuleSet;
        public string Name => "Markdown";
        public HighlightingRuleSet MainRuleSet => _mainRuleSet;
        public IEnumerable<HighlightingColor> NamedHighlightingColors => [];
        public IDictionary<string, string> Properties { get; } = new Dictionary<string, string>();
        public HighlightingColor GetNamedColor(string name) => null!;
        public HighlightingRuleSet GetNamedRuleSet(string name) => null!;
    }

    private void UpdateTabStyles()
    {
        var active   = _tabActiveBrush   ?? System.Windows.Media.Brushes.Gray;
        var inactive = _tabInactiveBrush ?? System.Windows.Media.Brushes.Transparent;
        var tab      = ViewModel.ActiveTab;

        PagesTabBtn.Background = tab == WikiTab.Pages ? active : inactive;
        QueryTabBtn.Background = tab == WikiTab.Query ? active : inactive;
        LintTabBtn.Background  = tab == WikiTab.Lint  ? active : inactive;
    }

    // ── Page tree selection ───────────────────────────────────────────────────

    private void OnPageItemSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is WikiTreeItem item)
        {
            ViewModel.SelectedTreeItem = item;
            if (sender is System.Windows.Controls.ListBox lb)
                lb.SelectedItem = null;
        }
    }

    // ── Query Enter key ──────────────────────────────────────────────────────

    private void OnQueryKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == WpfKey.Enter && !WpfKeyboard.Modifiers.HasFlag(WpfModifierKeys.Shift))
        {
            ViewModel.RunQueryCommand.Execute(null);
            e.Handled = true;
        }
    }

    // ── Import button ─────────────────────────────────────────────────────────

    private void OnImportClick(object sender, RoutedEventArgs e)
    {
        var dlg = new Win32OpenFileDialog
        {
            Title = "Select source files to import into Wiki",
            Filter = "Supported files|*.md;*.txt;*.pdf;*.docx|Markdown|*.md|Text|*.txt|PDF|*.pdf|Word|*.docx|All files|*.*",
            Multiselect = true
        };
        if (dlg.ShowDialog() != true) return;
        ViewModel.IngestSourceCommand.Execute(dlg.FileNames);
    }

    // ── Drag & drop ──────────────────────────────────────────────────────────

    private void OnDragEnter(object sender, WpfDragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(WpfDataFormats.FileDrop)
            ? WpfDragDropEffects.Copy
            : WpfDragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, WpfDragEventArgs e)
    {
        if (!e.Data.GetDataPresent(WpfDataFormats.FileDrop)) return;
        var files = (string[])e.Data.GetData(WpfDataFormats.FileDrop);
        if (files == null || files.Length == 0) return;

        var supportedFiles = files.Where(f =>
        {
            var ext = Path.GetExtension(f).ToLowerInvariant();
            return ext is ".md" or ".txt" or ".pdf" or ".docx";
        }).ToArray();

        if (supportedFiles.Length > 0)
        {
            ViewModel.IngestSourceCommand.Execute(supportedFiles);
        }
        else
        {
            ViewModel.StatusText = "No supported files found in drop.";
        }
        e.Handled = true;
    }
}
