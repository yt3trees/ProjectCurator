using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Avalonia;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;
using Microsoft.Extensions.DependencyInjection;
using ProjectCurator.Desktop.Helpers;
using ProjectCurator.Interfaces;
using ProjectCurator.Models;
using ProjectCurator.Services;
using ProjectCurator.ViewModels;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using InputModifiers = Avalonia.Input.KeyModifiers;

namespace ProjectCurator.Desktop.Views.Pages;

public partial class EditorPage : UserControl
{
    private TextEditor? _editor;
    private TextEditor? _diffViewer;
    private DiffLineBackgroundRenderer? _diffViewerRenderer;
    private EditorViewModel? _viewModel;
    private readonly IDialogService? _dialogService;
    private readonly FileEncodingService? _fileEncodingService;
    private DiffLineBackgroundRenderer? _diffRenderer;
    private bool _suppressTextSync;
    private bool _isInitialized;
    private string? _focusDiffBaseContent;
    private string? _focusDiffBaseLabel;

    public EditorPage()
    {
        InitializeComponent();
        DataContext = App.Services.GetService(typeof(EditorViewModel));
        _viewModel = DataContext as EditorViewModel;
        _dialogService = App.Services.GetService<IDialogService>();
        _fileEncodingService = App.Services.GetService<FileEncodingService>();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (_isInitialized)
            return;

        try
        {
            _editor = this.FindControl<TextEditor>("TextEditor");
            _diffViewer = this.FindControl<TextEditor>("DiffViewer");
            if (_editor == null) return;
            var diffPanel = this.FindControl<Grid>("DiffPanel");
            if (diffPanel != null)
                diffPanel.IsVisible = false;
            _editor.IsVisible = true;

            _ = _viewModel?.LoadProjectsAsync();

            TryApplyMarkdownHighlighting(_editor);

            // Register diff background renderer
            _diffRenderer = new DiffLineBackgroundRenderer();
            _editor.TextArea.TextView.BackgroundRenderers.Add(_diffRenderer);
            if (_diffViewer != null)
            {
                _diffViewerRenderer = new DiffLineBackgroundRenderer();
                _diffViewer.TextArea.TextView.BackgroundRenderers.Add(_diffViewerRenderer);
            }

            if (_viewModel != null)
            {
                _viewModel.RequestNewDecisionLogName = ShowTextInputDialogAsync;
                _viewModel.ShowScrollableError = (title, message) =>
                {
                    _ = _dialogService?.ShowMessageAsync(title, message);
                };
                _viewModel.RequestWorkstreamSelection = ShowWorkstreamSelectionDialogAsync;
                _viewModel.RequestFocusUpdateApproval = ShowFocusUpdateApprovalDialogAsync;
                _viewModel.RequestAiDecisionLogInput = ShowAiDecisionLogInputDialogAsync;
                _viewModel.RequestDecisionLogPreview = ShowDecisionLogPreviewDialogAsync;
                _viewModel.RequestMeetingNotesInput = ShowMeetingNotesInputDialogAsync;
                _viewModel.RequestMeetingNotesPreview = ShowMeetingNotesPreviewDialogAsync;

                _viewModel.PropertyChanged += OnViewModelPropertyChanged;
                _editor.TextChanged += OnEditorTextChanged;

                NoFileText.IsVisible = string.IsNullOrWhiteSpace(_viewModel.CurrentFile);
                _suppressTextSync = true;
                _editor.Text = _viewModel.EditorText ?? string.Empty;
                _suppressTextSync = false;
                ResetEditorViewport();
                UpdateEditorPanelsVisibility();
                RefreshDebugState();
            }

            _isInitialized = true;
        }
        catch (Exception ex)
        {
            _ = _dialogService?.ShowMessageAsync("Editor Initialization Error", ex.Message);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (_viewModel == null || _editor == null)
            return;

        if (args.PropertyName == nameof(EditorViewModel.CurrentFile))
        {
            _suppressTextSync = true;
            _editor.Text = _viewModel.EditorText ?? string.Empty;
            _suppressTextSync = false;
            ResetEditorViewport();
            NoFileText.IsVisible = string.IsNullOrWhiteSpace(_viewModel.CurrentFile);
            _focusDiffBaseContent = null;
            _focusDiffBaseLabel = null;
            RefreshDebugState();
            return;
        }

        if (args.PropertyName == nameof(EditorViewModel.IsDiffViewActive))
        {
            UpdateEditorPanelsVisibility();
            if (_viewModel.IsDiffViewActive)
                _ = UpdateDiffViewAsync();
            else
                (_focusDiffBaseContent, _focusDiffBaseLabel) = (null, null);
            RefreshDebugState();
            return;
        }

        if (args.PropertyName == nameof(EditorViewModel.EditorText) ||
            args.PropertyName == nameof(EditorViewModel.IsLoading))
        {
            RefreshDebugState();
        }
    }

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        if (_viewModel == null || _editor == null || _suppressTextSync)
            return;

        _viewModel.NotifyTextChanged(_editor.Text ?? string.Empty);
        if (_viewModel.IsDiffViewActive)
            _ = UpdateDiffViewAsync();
        RefreshDebugState();
    }

    private static void TryApplyMarkdownHighlighting(TextEditor editor)
    {
        var candidateUris = new[]
        {
            "avares://ProjectCurator/Assets/Markdown.xshd",
            "avares://ProjectCurator.Desktop/Assets/Markdown.xshd"
        };

        foreach (var uriText in candidateUris)
        {
            try
            {
                var uri = new Uri(uriText);
                using var stream = AssetLoader.Open(uri);
                using var reader = new XmlTextReader(stream);
                editor.SyntaxHighlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
                return;
            }
            catch
            {
                // Try next candidate URI.
            }
        }
    }

    private async void OnTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_viewModel == null)
            return;

        var node = FileTreeView.SelectedItem as FileTreeNode
            ?? e.AddedItems.OfType<FileTreeNode>().FirstOrDefault()
            ?? (e.Source as StyledElement)?.DataContext as FileTreeNode;

        if (node == null || node.IsDirectory)
            return;

        await OpenNodeAndSyncEditorAsync(node);
    }

    private void OnFileTreePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(FileTreeView);
        if (!point.Properties.IsRightButtonPressed && !point.Properties.IsLeftButtonPressed)
            return;

        var sourceControl = e.Source as Control;
        while (sourceControl != null)
        {
            if (sourceControl.DataContext is FileTreeNode node)
            {
                FileTreeView.SelectedItem = node;
                if (_viewModel != null)
                    _viewModel.SelectedNode = node;

                // SelectionChanged does not always raise consistently on TreeView templates.
                if (point.Properties.IsLeftButtonPressed && _viewModel != null && !node.IsDirectory)
                    _ = OpenNodeAndSyncEditorAsync(node);
                return;
            }
            sourceControl = sourceControl.Parent as Control;
        }
    }

    private void OnFileTreeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_viewModel == null) return;
        var sourceControl = e.Source as Control;
        while (sourceControl != null)
        {
            if (sourceControl.DataContext is FileTreeNode node && !node.IsDirectory)
            {
                _ = OpenNodeAndSyncEditorAsync(node);
                return;
            }
            sourceControl = sourceControl.Parent as Control;
        }
    }

    private async Task OpenNodeAndSyncEditorAsync(FileTreeNode node)
    {
        if (_viewModel == null) return;
        _viewModel.IsDiffViewActive = false;
        UpdateEditorPanelsVisibility();
        _viewModel.SelectedNode = node;
        await _viewModel.OpenFileAsync(node.FullPath);

        if (_editor != null)
        {
            _suppressTextSync = true;
            _editor.Text = _viewModel.EditorText ?? string.Empty;
            _suppressTextSync = false;
            ResetEditorViewport();
        }
        NoFileText.IsVisible = string.IsNullOrWhiteSpace(_viewModel.CurrentFile);
        RefreshDebugState();

        // If ViewModel could not switch file (for example confirm flow or dialog host issues),
        // force-load the selected file to keep editor usable.
        if (!string.Equals(_viewModel.CurrentFile, node.FullPath, StringComparison.OrdinalIgnoreCase) &&
            _fileEncodingService != null &&
            File.Exists(node.FullPath))
        {
            try
            {
                var (content, enc) = await _fileEncodingService.ReadFileAsync(node.FullPath);
                _viewModel.SuppressChangeEvent = true;
                _viewModel.EditorText = content;
                _viewModel.SuppressChangeEvent = false;
                _viewModel.CurrentFile = node.FullPath;
                _viewModel.Encoding = enc;
                _viewModel.IsDirty = false;
                _viewModel.IsDiffViewActive = false;
                if (_editor != null)
                {
                    _suppressTextSync = true;
                    _editor.Text = _viewModel.EditorText ?? string.Empty;
                    _suppressTextSync = false;
                    ResetEditorViewport();
                }
                RefreshDebugState();
            }
            catch (Exception ex)
            {
                await (_dialogService?.ShowMessageAsync("Open File Error", ex.Message) ?? Task.CompletedTask);
            }
        }
    }

    private void ResetEditorViewport()
    {
        if (_editor == null) return;
        try
        {
            _editor.CaretOffset = 0;
            _editor.ScrollToLine(1);
            _editor.ScrollTo(1, 1);
            _editor.InvalidateVisual();
            _editor.TextArea.TextView.InvalidateVisual();
        }
        catch
        {
            // Best-effort only.
        }
    }

    private void RefreshDebugState()
    {
        if (_viewModel == null)
            return;

        var vmLen = _viewModel.EditorText?.Length ?? 0;
        var editorLen = _editor?.Text?.Length ?? 0;
        var docLen = _editor?.Document?.TextLength ?? 0;
        var lineCount = _editor?.Document?.LineCount ?? 0;
        var nulCount = _viewModel.EditorText?.Count(c => c == '\0') ?? 0;
        var fileName = string.IsNullOrWhiteSpace(_viewModel.CurrentFile)
            ? "(none)"
            : Path.GetFileName(_viewModel.CurrentFile);
        var diff = _viewModel.IsDiffViewActive ? "on" : "off";
        var loading = _viewModel.IsLoading ? "on" : "off";
        var panel = _editor?.IsVisible == true ? "editor" : "diff";

        DebugStateText.Text = $"F:{fileName} VM:{vmLen} ED:{editorLen} DOC:{docLen}/{lineCount} NUL:{nulCount} D:{diff} L:{loading} P:{panel}";
    }

    private void OnProjectSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // Two-way binding handles selection; no-op to keep parity with WPF event flow.
    }

    private async Task UpdateDiffViewAsync()
    {
        if (_viewModel == null || _diffViewer == null || _diffViewerRenderer == null)
            return;

        var previous = _viewModel.OriginalContent ?? "";
        var current = _viewModel.EditorText ?? "";
        var fileName = string.IsNullOrWhiteSpace(_viewModel.CurrentFile)
            ? "(no file)"
            : Path.GetFileName(_viewModel.CurrentFile);
        var headerText = $"Diff: unsaved changes vs saved ({fileName})";

        if (string.Equals(fileName, "current_focus.md", StringComparison.OrdinalIgnoreCase))
        {
            if (_focusDiffBaseContent == null)
            {
                var historyDir = Path.Combine(Path.GetDirectoryName(_viewModel.CurrentFile!)!, "focus_history");
                var snapshots = GetFocusSnapshots(historyDir);
                if (snapshots.Count > 0)
                {
                    var selectedPath = await ShowSnapshotPickerDialogAsync(snapshots);
                    if (selectedPath == null)
                    {
                        _viewModel.IsDiffViewActive = false;
                        return;
                    }

                    _focusDiffBaseContent = await File.ReadAllTextAsync(selectedPath);
                    _focusDiffBaseLabel = Path.GetFileName(selectedPath);
                }
            }

            if (!string.IsNullOrWhiteSpace(_focusDiffBaseContent))
            {
                previous = _focusDiffBaseContent;
                headerText = $"Diff: current_focus.md vs {_focusDiffBaseLabel}";
            }
        }

        var (text, lines) = BuildSimpleDiff(previous, current);
        _diffViewer.Text = text;
        _diffViewerRenderer.SetDiffLines(lines);
        DiffHeaderText.Text = headerText;
        UpdateEditorPanelsVisibility();
    }

    private void UpdateEditorPanelsVisibility()
    {
        var diffPanel = this.FindControl<Grid>("DiffPanel");
        if (_editor == null || diffPanel == null || _viewModel == null)
            return;

        var showDiff = _viewModel.IsDiffViewActive;
        _editor.IsVisible = !showDiff;
        diffPanel.IsVisible = showDiff;
    }

    private static (string text, List<(int line, bool isAdd)> lines) BuildSimpleDiff(string previous, string current)
    {
        var prevLines = previous.Replace("\r\n", "\n").Split('\n');
        var currLines = current.Replace("\r\n", "\n").Split('\n');
        var max = Math.Max(prevLines.Length, currLines.Length);
        var output = new List<string>();
        var highlights = new List<(int line, bool isAdd)>();
        var outLine = 1;

        for (var i = 0; i < max; i++)
        {
            var hasPrev = i < prevLines.Length;
            var hasCurr = i < currLines.Length;
            var prev = hasPrev ? prevLines[i] : "";
            var curr = hasCurr ? currLines[i] : "";

            if (hasPrev && hasCurr)
            {
                if (string.Equals(prev, curr, StringComparison.Ordinal))
                {
                    output.Add($"  {curr}");
                    outLine++;
                }
                else
                {
                    output.Add($"- {prev}");
                    highlights.Add((outLine, false));
                    outLine++;
                    output.Add($"+ {curr}");
                    highlights.Add((outLine, true));
                    outLine++;
                }
            }
            else if (hasPrev)
            {
                output.Add($"- {prev}");
                highlights.Add((outLine, false));
                outLine++;
            }
            else
            {
                output.Add($"+ {curr}");
                highlights.Add((outLine, true));
                outLine++;
            }
        }

        return (string.Join(Environment.NewLine, output), highlights);
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel == null || _editor == null) return;
        if (e.Key == Key.Enter)
        {
            FindText(forward: !e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _viewModel.IsSearchBarVisible = false;
            _editor.Focus();
            e.Handled = true;
        }
    }

    private void OnSearchNext(object? sender, RoutedEventArgs e) => FindText(forward: true);
    private void OnSearchPrev(object? sender, RoutedEventArgs e) => FindText(forward: false);

    private void OnSearchClose(object? sender, RoutedEventArgs e)
    {
        if (_viewModel == null || _editor == null) return;
        _viewModel.IsSearchBarVisible = false;
        _editor.Focus();
    }

    private async void OnAddObsidianNote(object? sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;
        var selectedNode = FileTreeView.SelectedItem as FileTreeNode;
        if (!_viewModel.CanAddObsidianNote(selectedNode))
        {
            if (_dialogService != null)
                await _dialogService.ShowMessageAsync("Add Note", "Select a folder under obsidian_notes first.");
            return;
        }

        var fileName = await ShowTextInputDialogAsync("Add Obsidian Note", "File name (.md optional):");
        if (string.IsNullOrWhiteSpace(fileName)) return;

        var result = await _viewModel.CreateObsidianNoteAsync(selectedNode, fileName);
        if (!result.Ok && _dialogService != null)
            await _dialogService.ShowMessageAsync("Add Note", $"Failed to create note: {result.Error}");
    }

    private async void OnDeleteNode(object? sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;
        var selectedNode = FileTreeView.SelectedItem as FileTreeNode;
        if (_viewModel.CanDeleteObsidianNote(selectedNode))
        {
            if (_dialogService != null)
            {
                var ok = await _dialogService.ShowConfirmAsync("Delete", $"Delete '{selectedNode!.Name}'?");
                if (!ok) return;
            }

            var result = _viewModel.DeleteObsidianNote(selectedNode);
            if (!result.Ok && _dialogService != null)
                await _dialogService.ShowMessageAsync("Delete", $"Failed to delete: {result.Error}");
            return;
        }

        if (selectedNode != null && !selectedNode.IsDirectory &&
            selectedNode.FullPath.Contains("decision_log", StringComparison.OrdinalIgnoreCase))
        {
            await _viewModel.DeleteDecisionLog(selectedNode);
        }
    }

    private void OnFileTreeContextMenuOpened(object? sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;
        var selectedNode = FileTreeView.SelectedItem as FileTreeNode;
        AddObsidianNoteMenuItem.IsEnabled = _viewModel.CanAddObsidianNote(selectedNode);
        DeleteNodeMenuItem.IsEnabled = _viewModel.CanDeleteObsidianNote(selectedNode) ||
            (selectedNode != null && !selectedNode.IsDirectory &&
             selectedNode.FullPath.Contains("decision_log", StringComparison.OrdinalIgnoreCase));
    }

    private async void OnEditorContextMenuOpened(object? sender, RoutedEventArgs e)
    {
        if (_editor == null || sender is not ContextMenu menu)
            return;

        menu.Items.Clear();
        var folders = await Task.Run(FindWorkFolders);
        if (folders.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = "No work folders found", IsEnabled = false });
            return;
        }

        menu.Items.Add(new MenuItem { Header = "Insert Work Folder Path", IsEnabled = false });
        menu.Items.Add(new Separator());
        foreach (var folder in folders)
        {
            var fileName = Path.GetFileName(folder);
            var item = new MenuItem { Header = fileName };
            var captured = folder;
            item.Click += (_, _) => InsertWorkFolderLink(captured);
            menu.Items.Add(item);
        }
    }

    private List<string> FindWorkFolders()
    {
        if (_viewModel == null || string.IsNullOrWhiteSpace(_viewModel.CurrentFile))
            return [];

        var startDir = Path.GetDirectoryName(_viewModel.CurrentFile);
        if (string.IsNullOrWhiteSpace(startDir))
            return [];

        string? projectRoot = null;
        var current = new DirectoryInfo(startDir);
        while (current != null)
        {
            var sharedWork = Path.Combine(current.FullName, "shared", "_work");
            if (Directory.Exists(sharedWork))
            {
                projectRoot = current.FullName;
                break;
            }
            current = current.Parent;
        }

        if (projectRoot == null) return [];

        var workRoot = Path.Combine(projectRoot, "shared", "_work");
        var pattern = new Regex(@"^\d{8}_", RegexOptions.Compiled);

        return Directory
            .EnumerateDirectories(workRoot, "*", SearchOption.AllDirectories)
            .Where(path => pattern.IsMatch(Path.GetFileName(path)))
            .OrderByDescending(Path.GetFileName)
            .Take(30)
            .ToList();
    }

    private void InsertWorkFolderLink(string folderPath)
    {
        if (_editor == null) return;
        var name = Path.GetFileName(folderPath);
        _editor.Document.Insert(_editor.CaretOffset, $"[{name}]({folderPath})");
        _editor.Focus();
    }

    private void OnEditorPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_editor == null || _viewModel == null) return;
        if (!e.KeyModifiers.HasFlag(InputModifiers.Control)) return;
        if (!e.GetCurrentPoint(_editor).Properties.IsLeftButtonPressed) return;

        var position = _editor.GetPositionFromPoint(e.GetPosition(_editor));
        if (!position.HasValue) return;

        var offset = _editor.Document.GetOffset(position.Value.Line, position.Value.Column);
        var line = _editor.Document.GetLineByOffset(offset);
        var lineText = _editor.Document.GetText(line.Offset, line.Length);
        var lineOffset = offset - line.Offset;

        var linkRegex = new Regex(@"\[[^\]]*\]\(([^)]+)\)");
        foreach (Match match in linkRegex.Matches(lineText))
        {
            if (!match.Success) continue;
            var linkBodyGroup = match.Groups[1];
            if (lineOffset < linkBodyGroup.Index || lineOffset > linkBodyGroup.Index + linkBodyGroup.Length)
                continue;

            var target = linkBodyGroup.Value.Trim();
            if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                target.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                App.Services.GetService<IShellService>()?.OpenFile(target);
                e.Handled = true;
                return;
            }

            var absolutePath = Path.IsPathRooted(target)
                ? target
                : (string.IsNullOrWhiteSpace(_viewModel.CurrentFile)
                    ? ""
                    : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(_viewModel.CurrentFile)!, target)));

            if (string.IsNullOrWhiteSpace(absolutePath)) return;
            var shellService = App.Services.GetService<IShellService>();
            if (Directory.Exists(absolutePath))
            {
                shellService?.OpenFolder(absolutePath);
                e.Handled = true;
                return;
            }

            if (File.Exists(absolutePath))
            {
                shellService?.OpenFile(absolutePath);
                e.Handled = true;
            }
            return;
        }
    }

    private void OnEditorPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (_editor == null) return;
        if (!e.KeyModifiers.HasFlag(InputModifiers.Shift)) return;

        var scrollViewer = _editor.TextArea.TextView.GetVisualDescendants()
            .OfType<ScrollViewer>()
            .FirstOrDefault();
        if (scrollViewer == null || scrollViewer.Extent.Width <= scrollViewer.Viewport.Width)
            return;

        var delta = e.Delta.Y;
        var horizontalStep = 48d;
        var direction = delta > 0 ? -1d : 1d;
        var next = scrollViewer.Offset.X + (direction * horizontalStep);
        scrollViewer.Offset = new Vector(next, scrollViewer.Offset.Y);
        e.Handled = true;
    }

    private void FindText(bool forward)
    {
        if (_viewModel == null || _editor == null) return;
        var query = _viewModel.SearchText;
        if (string.IsNullOrEmpty(query)) return;

        var text = _editor.Text ?? "";
        var start = _editor.CaretOffset;

        int idx = forward
            ? text.IndexOf(query, Math.Min(text.Length, start + 1), StringComparison.OrdinalIgnoreCase)
            : text.LastIndexOf(query, Math.Max(0, start - 1), StringComparison.OrdinalIgnoreCase);

        if (idx < 0)
        {
            idx = forward
                ? text.IndexOf(query, StringComparison.OrdinalIgnoreCase)
                : text.LastIndexOf(query, StringComparison.OrdinalIgnoreCase);
        }

        if (idx >= 0)
        {
            _editor.Select(idx, query.Length);
            _editor.CaretOffset = idx + query.Length;
            _editor.TextArea.Caret.BringCaretToView();
            _editor.Focus();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_viewModel == null) return;
        if (e.KeyModifiers != KeyModifiers.Control) return;

        switch (e.Key)
        {
            case Key.F:
                _viewModel.ToggleSearchBarCommand.Execute(null);
                if (_viewModel.IsSearchBarVisible)
                    SearchTextBox.Focus();
                e.Handled = true;
                break;
            case Key.D:
                if (_viewModel.ToggleDiffViewCommand.CanExecute(null))
                    _viewModel.ToggleDiffViewCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private Task<string?> ShowTextInputDialogAsync()
        => ShowTextInputDialogAsync("New Decision Log", "File name (date is added automatically):");

    private async Task<string?> ShowTextInputDialogAsync(string title, string prompt)
    {
        var input = new TextBox
        {
            Watermark = "Enter value...",
            MinWidth = 300
        };

        var dialog = new Window
        {
            Title = title,
            Width = 420,
            Height = 170,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(16),
                Spacing = 10,
                Children =
                {
                    new TextBlock { Text = prompt },
                    input,
                    new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        Spacing = 8,
                        Children =
                        {
                            new Button { Name = "OkBtn", Content = "Create", MinWidth = 80 },
                            new Button { Name = "CancelBtn", Content = "Cancel", MinWidth = 80 }
                        }
                    }
                }
            }
        };

        var okBtn = dialog.FindControl<Button>("OkBtn");
        var cancelBtn = dialog.FindControl<Button>("CancelBtn");
        var tcs = new TaskCompletionSource<string?>();
        okBtn!.Click += (_, _) => { tcs.TrySetResult(input.Text?.Trim()); dialog.Close(); };
        cancelBtn!.Click += (_, _) => { tcs.TrySetResult(null); dialog.Close(); };
        dialog.Closed += (_, _) => tcs.TrySetResult(null);

        var owner = this.VisualRoot as Window;
        if (owner != null)
            await dialog.ShowDialog(owner);
        else
            dialog.Show();

        return await tcs.Task;
    }

    private async Task<(bool ok, string? wsId)> ShowWorkstreamSelectionDialogAsync(List<WorkstreamInfo> workstreams)
    {
        var owner = this.VisualRoot as Window;
        if (owner == null) return (false, null);

        var combo = new ComboBox
        {
            MinWidth = 300,
            ItemsSource = new[] { "(General)" }.Concat(workstreams.Select(w => $"{w.Id} - {w.Label}")).ToList(),
            SelectedIndex = 0
        };
        var result = await ShowDialogWithButtonsAsync(owner, "Select Workstream", combo, "Continue", "Cancel");
        if (!result) return (false, null);
        if (combo.SelectedIndex <= 0) return (true, null);
        return (true, workstreams[combo.SelectedIndex - 1].Id);
    }

    private async Task<(bool apply, string? content)> ShowFocusUpdateApprovalDialogAsync(
        FocusUpdateResult result,
        Func<string, string, Task<string>> refineFunc)
    {
        var owner = this.VisualRoot as Window;
        if (owner == null) return (false, null);

        var contentBox = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 420,
            Text = result.ProposedContent
        };

        var infoText = new TextBlock
        {
            Text = "Review focus update proposal. You can edit before applying.",
            TextWrapping = TextWrapping.Wrap
        };

        var refineBtn = new Button { Content = "Refine", MinWidth = 90 };
        var applyBtn = new Button { Content = "Apply", MinWidth = 90 };
        var cancelBtn = new Button { Content = "Cancel", MinWidth = 90 };
        var tcs = new TaskCompletionSource<(bool apply, string? content)>();

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Avalonia.Thickness(0, 10, 0, 0),
            Children =
            {
                refineBtn,
                applyBtn,
                cancelBtn
            }
        };

        var panel = new StackPanel { Spacing = 8, Margin = new Avalonia.Thickness(12) };
        panel.Children.Add(infoText);
        panel.Children.Add(contentBox);
        panel.Children.Add(footer);

        var dialog = new Window
        {
            Title = "Review Focus Update",
            Width = 860,
            Height = 660,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = panel
        };

        refineBtn.Click += async (_, _) =>
        {
            refineBtn.IsEnabled = false;
            try
            {
                var instructions = await ShowTextInputDialogAsync("Refine Instructions", "How should AI refine this proposal?");
                if (string.IsNullOrWhiteSpace(instructions))
                    return;

                var refined = await refineFunc(contentBox.Text ?? "", instructions);
                if (!string.IsNullOrWhiteSpace(refined))
                    contentBox.Text = refined;
            }
            catch (Exception ex)
            {
                await (_dialogService?.ShowMessageAsync("Refine Error", ex.Message) ?? Task.CompletedTask);
            }
            finally
            {
                refineBtn.IsEnabled = true;
            }
        };
        applyBtn.Click += (_, _) => { tcs.TrySetResult((true, contentBox.Text)); dialog.Close(); };
        cancelBtn.Click += (_, _) => { tcs.TrySetResult((false, null)); dialog.Close(); };
        dialog.Closed += (_, _) => tcs.TrySetResult((false, null));

        await dialog.ShowDialog(owner);
        return await tcs.Task;
    }

    private async Task<AiDecisionLogInputResult?> ShowAiDecisionLogInputDialogAsync(
        List<DetectedDecision> candidates,
        string? prefillText = null)
    {
        var owner = this.VisualRoot as Window;
        if (owner == null) return null;

        var input = new TextBox
        {
            AcceptsReturn = true,
            Height = 200,
            TextWrapping = TextWrapping.Wrap,
            Text = prefillText ?? ""
        };
        var useBlank = new CheckBox { Content = "Use blank template", IsChecked = false };
        var statusConfirmed = new RadioButton { Content = "Confirmed", IsChecked = true };
        var statusTentative = new RadioButton { Content = "Tentative", GroupName = "DecisionStatus" };
        var triggerCombo = new ComboBox
        {
            MinWidth = 180,
            ItemsSource = new[] { "Solo decision", "AI session", "Meeting" },
            SelectedIndex = 0
        };
        var attachedFiles = new List<string>();
        var attachedFilesList = new StackPanel { Spacing = 4 };
        var attachButton = new Button { Content = "Attach file...", MinWidth = 130 };

        var candidateChecks = new StackPanel { Spacing = 4 };
        foreach (var candidate in candidates)
        {
            var statusTag = string.Equals(candidate.Status, "tentative", StringComparison.OrdinalIgnoreCase)
                ? " (tentative)"
                : "";
            var check = new CheckBox
            {
                Content = $"{candidate.Summary}{statusTag}",
                IsChecked = candidate.IsSelected
            };
            check.IsCheckedChanged += (_, _) => candidate.IsSelected = check.IsChecked == true;
            candidateChecks.Children.Add(check);
        }

        attachButton.Click += async (_, _) =>
        {
            var topLevel = TopLevel.GetTopLevel(owner);
            if (topLevel?.StorageProvider == null)
                return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                AllowMultiple = true,
                Title = "Attach files",
                FileTypeFilter =
                [
                    FilePickerFileTypes.TextPlain,
                    new FilePickerFileType("Markdown") { Patterns = ["*.md", "*.markdown"] }
                ]
            });

            foreach (var file in files)
            {
                var localPath = file.TryGetLocalPath();
                if (string.IsNullOrWhiteSpace(localPath) || attachedFiles.Contains(localPath))
                    continue;

                attachedFiles.Add(localPath);
                var fileLabel = new TextBlock
                {
                    Text = Path.GetFileName(localPath),
                    VerticalAlignment = VerticalAlignment.Center
                };
                var removeBtn = new Button
                {
                    Content = "Remove",
                    MinWidth = 70
                };
                var row = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children = { fileLabel, removeBtn }
                };
                removeBtn.Click += (_, _) =>
                {
                    attachedFiles.Remove(localPath);
                    attachedFilesList.Children.Remove(row);
                };
                attachedFilesList.Children.Add(row);
            }
        };

        var panel = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "Input decision context (editable)." },
                input,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 10,
                    Children =
                    {
                        new TextBlock { Text = "Status:", VerticalAlignment = VerticalAlignment.Center },
                        statusConfirmed,
                        statusTentative
                    }
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 10,
                    Children =
                    {
                        new TextBlock { Text = "Trigger:", VerticalAlignment = VerticalAlignment.Center },
                        triggerCombo
                    }
                },
                new TextBlock { Text = "Attach files (optional)" },
                attachButton,
                attachedFilesList,
                new TextBlock { Text = "Detected candidates", IsVisible = candidates.Count > 0 },
                new Border
                {
                    BorderBrush = Brushes.Gray,
                    BorderThickness = new Avalonia.Thickness(1),
                    Padding = new Avalonia.Thickness(8),
                    MaxHeight = 180,
                    Child = new ScrollViewer { Content = candidateChecks },
                    IsVisible = candidates.Count > 0
                },
                useBlank
            }
        };

        var res = await ShowDialogWithButtonsAsync(owner, "AI Decision Log", panel, "Continue", "Cancel");
        if (!res) return null;

        return new AiDecisionLogInputResult
        {
            UseBlankTemplate = useBlank.IsChecked == true,
            UserInput = input.Text ?? "",
            Status = statusTentative.IsChecked == true ? "Tentative" : "Confirmed",
            Trigger = triggerCombo.SelectedItem?.ToString() ?? "Solo decision",
            SelectedCandidates = candidates.Where(c => c.IsSelected).ToList(),
            AttachedFilePaths = attachedFiles
        };
    }

    private async Task<(bool save, string? content, string? fileName, bool removeTension)> ShowDecisionLogPreviewDialogAsync(
        DecisionLogDraftResult draft,
        Func<string, string, Task<string>> refineFunc)
    {
        var owner = this.VisualRoot as Window;
        if (owner == null) return (false, null, null, false);

        var fileNameBox = new TextBox
        {
            Text = draft.SuggestedFileName,
            Watermark = "file name"
        };
        var contentBox = new TextBox
        {
            AcceptsReturn = true,
            Height = 420,
            TextWrapping = TextWrapping.Wrap,
            Text = draft.DraftContent
        };
        var removeTension = new CheckBox
        {
            Content = "Remove resolved tension",
            IsChecked = !string.IsNullOrWhiteSpace(draft.ResolvedTension)
        };

        var refineBtn = new Button { Content = "Refine", MinWidth = 90 };
        var saveBtn = new Button { Content = "Save", MinWidth = 90 };
        var cancelBtn = new Button { Content = "Cancel", MinWidth = 90 };
        var tcs = new TaskCompletionSource<(bool save, string? content, string? fileName, bool removeTension)>();

        var panel = new StackPanel
        {
            Spacing = 8,
            Margin = new Avalonia.Thickness(12),
            Children =
            {
                new TextBlock { Text = "File name" },
                fileNameBox,
                new TextBlock { Text = "Content" },
                contentBox,
                removeTension,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { refineBtn, saveBtn, cancelBtn }
                }
            }
        };

        var dialog = new Window
        {
            Title = "Decision Log Preview",
            Width = 860,
            Height = 700,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = panel
        };

        refineBtn.Click += async (_, _) =>
        {
            refineBtn.IsEnabled = false;
            try
            {
                var instructions = await ShowTextInputDialogAsync("Refine Instructions", "How should AI refine this decision log?");
                if (string.IsNullOrWhiteSpace(instructions))
                    return;

                var refined = await refineFunc(contentBox.Text ?? "", instructions);
                if (!string.IsNullOrWhiteSpace(refined))
                    contentBox.Text = refined;
            }
            catch (Exception ex)
            {
                await (_dialogService?.ShowMessageAsync("Refine Error", ex.Message) ?? Task.CompletedTask);
            }
            finally
            {
                refineBtn.IsEnabled = true;
            }
        };
        saveBtn.Click += (_, _) =>
        {
            tcs.TrySetResult((true, contentBox.Text, fileNameBox.Text, removeTension.IsChecked == true));
            dialog.Close();
        };
        cancelBtn.Click += (_, _) =>
        {
            tcs.TrySetResult((false, null, null, false));
            dialog.Close();
        };
        dialog.Closed += (_, _) => tcs.TrySetResult((false, null, null, false));

        await dialog.ShowDialog(owner);
        return await tcs.Task;
    }

    private async Task<MeetingNotesInputResult?> ShowMeetingNotesInputDialogAsync(
        ProjectInfo project,
        List<WorkstreamInfo> activeWorkstreams)
    {
        var owner = this.VisualRoot as Window;
        if (owner == null) return null;

        var wsItems = new[] { "(General)" }.Concat(activeWorkstreams.Select(w => $"{w.Id} - {w.Label}")).ToList();
        var wsCombo = new ComboBox { ItemsSource = wsItems, SelectedIndex = 0 };
        var notesBox = new TextBox
        {
            AcceptsReturn = true,
            Height = 280,
            TextWrapping = TextWrapping.Wrap,
        };

        var panel = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = $"Project: {project.DisplayName}" },
                new TextBlock { Text = "Workstream" },
                wsCombo,
                new TextBlock { Text = "Meeting notes" },
                notesBox
            }
        };

        var res = await ShowDialogWithButtonsAsync(owner, "Import Meeting Notes", panel, "Analyze", "Cancel");
        if (!res) return null;

        var wsId = wsCombo.SelectedIndex <= 0 ? null : activeWorkstreams[wsCombo.SelectedIndex - 1].Id;
        return new MeetingNotesInputResult
        {
            MeetingNotes = notesBox.Text ?? "",
            WorkstreamId = wsId
        };
    }

    private async Task<bool> ShowMeetingNotesPreviewDialogAsync(
        MeetingAnalysisResult result,
        ProjectInfo project,
        string? workstreamId)
    {
        var owner = this.VisualRoot as Window;
        if (owner == null) return false;
        var previewBox = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Text = BuildMeetingPreviewText(result, project, workstreamId)
        };

        var decisionMaster = new CheckBox
        {
            Content = $"Apply decisions ({result.Decisions.Count})",
            IsChecked = result.Decisions.Any(d => d.IsSelected)
        };
        var focusMaster = new CheckBox
        {
            Content = $"Apply focus update ({result.FocusUpdate.NextActions.Count + result.FocusUpdate.RecentContext.Count} items)",
            IsChecked = result.FocusUpdate.IsSelected
        };
        var tensionMaster = new CheckBox
        {
            Content = $"Apply tensions ({result.Tensions.TechnicalQuestions.Count + result.Tensions.Tradeoffs.Count + result.Tensions.Concerns.Count} items)",
            IsChecked = result.Tensions.IsSelected
        };
        var asanaMaster = new CheckBox
        {
            Content = $"Apply Asana tasks ({result.AsanaTasks.Tasks.Count})",
            IsChecked = result.AsanaTasks.IsSelected
        };

        var decisionList = new StackPanel { Spacing = 4, Margin = new Avalonia.Thickness(14, 2, 0, 4) };
        foreach (var decision in result.Decisions)
        {
            var checkbox = new CheckBox
            {
                Content = $"{decision.Title} [{decision.Status}]",
                IsChecked = decision.IsSelected
            };
            var toggleDraftBtn = new Button
            {
                Content = "Show draft",
                MinWidth = 90,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            var draftViewer = new TextBox
            {
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                Text = MeetingNotesService.BuildDecisionLogContent(decision),
                IsVisible = false,
                Margin = new Avalonia.Thickness(16, 2, 0, 4),
                MinHeight = 120
            };
            toggleDraftBtn.Click += (_, _) =>
            {
                draftViewer.IsVisible = !draftViewer.IsVisible;
                toggleDraftBtn.Content = draftViewer.IsVisible ? "Hide draft" : "Show draft";
            };
            checkbox.IsCheckedChanged += (_, _) =>
            {
                decision.IsSelected = checkbox.IsChecked == true;
                decisionMaster.IsChecked = result.Decisions.Any(d => d.IsSelected);
                previewBox.Text = BuildMeetingPreviewText(result, project, workstreamId);
            };
            decisionList.Children.Add(checkbox);
            decisionList.Children.Add(toggleDraftBtn);
            decisionList.Children.Add(draftViewer);
        }

        var asanaTaskList = new StackPanel { Spacing = 4, Margin = new Avalonia.Thickness(14, 2, 0, 4) };
        foreach (var task in result.AsanaTasks.Tasks)
        {
            var checkbox = new CheckBox
            {
                Content = string.IsNullOrWhiteSpace(task.Priority) ? task.Title : $"{task.Title} ({task.Priority})",
                IsChecked = task.IsSelected
            };
            checkbox.IsCheckedChanged += (_, _) =>
            {
                task.IsSelected = checkbox.IsChecked == true;
                asanaMaster.IsChecked = result.AsanaTasks.Tasks.Any(t => t.IsSelected);
                previewBox.Text = BuildMeetingPreviewText(result, project, workstreamId);
            };
            asanaTaskList.Children.Add(checkbox);
        }

        decisionMaster.IsCheckedChanged += (_, _) =>
        {
            var selected = decisionMaster.IsChecked == true;
            foreach (var decision in result.Decisions)
                decision.IsSelected = selected;
            foreach (var control in decisionList.Children.OfType<CheckBox>())
                control.IsChecked = selected;
            previewBox.Text = BuildMeetingPreviewText(result, project, workstreamId);
        };
        focusMaster.IsCheckedChanged += (_, _) =>
        {
            result.FocusUpdate.IsSelected = focusMaster.IsChecked == true;
            previewBox.Text = BuildMeetingPreviewText(result, project, workstreamId);
        };
        tensionMaster.IsCheckedChanged += (_, _) =>
        {
            result.Tensions.IsSelected = tensionMaster.IsChecked == true;
            previewBox.Text = BuildMeetingPreviewText(result, project, workstreamId);
        };
        asanaMaster.IsCheckedChanged += (_, _) =>
        {
            var selected = asanaMaster.IsChecked == true;
            result.AsanaTasks.IsSelected = selected;
            foreach (var task in result.AsanaTasks.Tasks)
                task.IsSelected = selected;
            foreach (var control in asanaTaskList.Children.OfType<CheckBox>())
                control.IsChecked = selected;
            previewBox.Text = BuildMeetingPreviewText(result, project, workstreamId);
        };

        var leftPane = new ScrollViewer
        {
            Content = new StackPanel
            {
                Spacing = 4,
                Margin = new Avalonia.Thickness(8),
                Children =
                {
                    decisionMaster,
                    decisionList,
                    focusMaster,
                    tensionMaster,
                    asanaMaster,
                    asanaTaskList
                }
            }
        };

        var body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("320,*"),
            RowDefinitions = new RowDefinitions("*"),
            Children =
            {
                leftPane,
                new ScrollViewer
                {
                    Content = previewBox
                }
            }
        };
        Grid.SetColumn(body.Children[1], 1);

        var res = await ShowDialogWithButtonsAsync(owner, "Meeting Notes Preview", body, "Apply", "Cancel");
        return res;
    }

    private static string BuildMeetingPreviewText(MeetingAnalysisResult result, ProjectInfo project, string? workstreamId)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Meeting Analysis Preview");
        sb.AppendLine("========================");
        sb.AppendLine($"Project: {project.DisplayName}");
        sb.AppendLine($"Workstream: {(string.IsNullOrWhiteSpace(workstreamId) ? "General" : workstreamId)}");
        sb.AppendLine();

        var selectedDecisions = result.Decisions.Where(d => d.IsSelected).ToList();
        sb.AppendLine($"Decisions ({selectedDecisions.Count}/{result.Decisions.Count})");
        foreach (var decision in selectedDecisions)
            sb.AppendLine($"- {decision.Title} [{decision.Status}]");
        if (selectedDecisions.Count == 0) sb.AppendLine("- (none)");
        sb.AppendLine();

        var focusItems = result.FocusUpdate.RecentContext.Concat(result.FocusUpdate.NextActions).ToList();
        sb.AppendLine($"Focus Update ({(result.FocusUpdate.IsSelected ? "apply" : "skip")}, {focusItems.Count} items)");
        if (result.FocusUpdate.IsSelected)
        {
            foreach (var item in focusItems.Take(12))
                sb.AppendLine($"- {item}");
            if (focusItems.Count > 12)
                sb.AppendLine($"- ... and {focusItems.Count - 12} more");
        }
        sb.AppendLine();

        var tensions = result.Tensions.TechnicalQuestions
            .Concat(result.Tensions.Tradeoffs)
            .Concat(result.Tensions.Concerns)
            .ToList();
        sb.AppendLine($"Tensions ({(result.Tensions.IsSelected ? "apply" : "skip")}, {tensions.Count} items)");
        if (result.Tensions.IsSelected)
        {
            foreach (var tension in tensions.Take(12))
                sb.AppendLine($"- {tension}");
            if (tensions.Count > 12)
                sb.AppendLine($"- ... and {tensions.Count - 12} more");
        }
        sb.AppendLine();

        var selectedTasks = result.AsanaTasks.Tasks.Where(t => t.IsSelected).ToList();
        sb.AppendLine($"Asana Tasks ({(result.AsanaTasks.IsSelected ? "apply" : "skip")}, {selectedTasks.Count}/{result.AsanaTasks.Tasks.Count})");
        if (result.AsanaTasks.IsSelected)
        {
            foreach (var task in selectedTasks.Take(16))
            {
                var priority = string.IsNullOrWhiteSpace(task.Priority) ? "" : $" ({task.Priority})";
                sb.AppendLine($"- {task.Title}{priority}");
            }
            if (selectedTasks.Count > 16)
                sb.AppendLine($"- ... and {selectedTasks.Count - 16} more");
        }
        sb.AppendLine();
        sb.AppendLine("Apply selected updates?");

        return sb.ToString();
    }

    private static async Task<bool> ShowDialogWithButtonsAsync(
        Window owner,
        string title,
        Control body,
        string okText,
        string cancelText)
    {
        var ok = new Button { Name = "OkBtn", Content = okText, MinWidth = 90 };
        var cancel = new Button { Name = "CancelBtn", Content = cancelText, MinWidth = 90 };
        var footer = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Avalonia.Thickness(12),
            Children = { ok, cancel }
        };
        Grid.SetRow(footer, 1);

        var dialog = new Window
        {
            Title = title,
            Width = 760,
            Height = 620,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new Grid
            {
                RowDefinitions = new RowDefinitions("*,Auto"),
                Children =
                {
                    body,
                    footer
                }
            }
        };

        var tcs = new TaskCompletionSource<bool>();
        ok.Click += (_, _) => { tcs.TrySetResult(true); dialog.Close(); };
        cancel.Click += (_, _) => { tcs.TrySetResult(false); dialog.Close(); };
        dialog.Closed += (_, _) => tcs.TrySetResult(false);

        await dialog.ShowDialog(owner);
        return await tcs.Task;
    }

    private static List<string> GetFocusSnapshots(string histDir)
    {
        if (!Directory.Exists(histDir)) return [];
        return Directory.GetFiles(histDir, "*.md")
            .OrderByDescending(Path.GetFileNameWithoutExtension)
            .ToList();
    }

    private async Task<string?> ShowSnapshotPickerDialogAsync(List<string> snapshots)
    {
        var owner = this.VisualRoot as Window;
        if (owner == null) return null;

        var names = snapshots.Select(Path.GetFileName).ToList();
        var combo = new ComboBox
        {
            MinWidth = 400,
            ItemsSource = names,
            SelectedIndex = 0
        };

        var body = new StackPanel
        {
            Spacing = 8,
            Margin = new Avalonia.Thickness(12),
            Children =
            {
                new TextBlock { Text = "Select a focus_history snapshot." },
                combo
            }
        };

        var ok = await ShowDialogWithButtonsAsync(owner, "Select Snapshot", body, "Compare", "Cancel");
        if (!ok || combo.SelectedIndex < 0 || combo.SelectedIndex >= snapshots.Count)
            return null;

        return snapshots[combo.SelectedIndex];
    }
}
