using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Wpf.Ui;
using Wpf.Ui.Controls;
using System.Threading;
using ProjectCurator.Models;
using ProjectCurator.Services;
using ProjectCurator.Views.Pages;
using TextBlock = System.Windows.Controls.TextBlock;

namespace ProjectCurator.ViewModels;

// ファイルツリーのノードモデル
public partial class FileTreeNode : ObservableObject
{
    public string Name { get; set; } = "";
    public string FullPath { get; set; } = "";
    public bool IsDirectory { get; set; }
    public bool IsClosedWorkstream { get; set; }
    public ObservableCollection<FileTreeNode> Children { get; } = [];

    [ObservableProperty]
    private bool isExpanded;

    [ObservableProperty]
    private bool isSelected;
}

public partial class EditorViewModel : ObservableObject
{
    private readonly FileEncodingService _encodingService;
    private readonly ProjectDiscoveryService _discoveryService;
    private readonly FocusUpdateService _focusUpdateService;
    private readonly LlmClientService _llmClient;
    private readonly ConfigService _configService;
    private string? _pendingFileToOpen;
    private bool _suppressAutoFileOpenOnProjectChange;
    private CancellationTokenSource? _focusUpdateCts;

    // ---- 選択プロジェクト ----
    [ObservableProperty]
    private ObservableCollection<ProjectInfo> projects = [];

    [ObservableProperty]
    private ProjectInfo? selectedProject;

    // ---- ファイルツリー ----
    [ObservableProperty]
    private ObservableCollection<FileTreeNode> treeNodes = [];

    [ObservableProperty]
    private FileTreeNode? selectedNode;

    // ---- エディタ状態 ----
    [ObservableProperty]
    private string currentFile = "";

    [ObservableProperty]
    private string editorText = "";

    private string _originalContent = "";

    [ObservableProperty]
    private bool isDirty;

    [ObservableProperty]
    private bool isDiffViewActive;

    [ObservableProperty]
    private string encoding = "UTF8";

    [ObservableProperty]
    private bool suppressChangeEvent;

    // ---- 検索 ----
    [ObservableProperty]
    private bool isSearchBarVisible;

    [ObservableProperty]
    private string searchText = "";

    // ---- ローディング ----
    [ObservableProperty]
    private bool isLoading;

    // ---- AI 機能 ----
    [ObservableProperty]
    private bool isAiEnabled;

    // ---- new decision_log 要求コールバック ----
    public Func<Task<string?>>? RequestNewDecisionLogName;

    // ---- Update Focus ダイアログコールバック ----
    // workstream 選択: workstream リストを渡す → (ok=false でキャンセル, ok=true で wsId=null:general / wsId=id:workstream)
    public Func<List<WorkstreamInfo>, Task<(bool ok, string? wsId)>>? RequestWorkstreamSelection;
    // 更新提案を表示 → (適用するか, 適用コンテンツ) を返す
    // 第2引数: refineFunc(currentProposed, instructions) → 改訂後全文
    public Func<FocusUpdateResult, Func<string, string, Task<string>>, Task<(bool apply, string? content)>>? RequestFocusUpdateApproval;
    // スクロール可能なエラーダイアログ
    public Action<string, string>? ShowScrollableError;

    public string OriginalContent => _originalContent;

    public EditorViewModel(
        FileEncodingService encodingService,
        ProjectDiscoveryService discoveryService,
        FocusUpdateService focusUpdateService,
        LlmClientService llmClient,
        ConfigService configService)
    {
        _encodingService = encodingService;
        _discoveryService = discoveryService;
        _focusUpdateService = focusUpdateService;
        _llmClient = llmClient;
        _configService = configService;

        IsAiEnabled = _configService.LoadSettings().AiEnabled;
        WeakReferenceMessenger.Default.Register<AiEnabledChangedMessage>(this,
            (_, msg) => IsAiEnabled = msg.Enabled);
    }

    // =====================================================================
    // プロジェクト一覧ロード
    // =====================================================================
    public async Task LoadProjectsAsync()
    {
        IsLoading = true;
        try
        {
            var list = await _discoveryService.GetProjectInfoListAsync();
            var currentSelectionKey = SelectedProject?.HiddenKey;

            Projects.Clear();
            foreach (var p in list)
                Projects.Add(p);

            // 選択状態を復元
            if (currentSelectionKey != null)
            {
                var match = Projects.FirstOrDefault(p => p.HiddenKey == currentSelectionKey);
                if (match != null)
                {
                    SelectedProject = match;
                }
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    // =====================================================================
    // プロジェクト選択変更 -> ツリー再構築
    // =====================================================================
    partial void OnSelectedProjectChanged(ProjectInfo? value)
    {
        if (value == null) return;
        IsDiffViewActive = false;
        BuildFileTree(value);
        UpdateStatus();
        UpdateFocusCommand.NotifyCanExecuteChanged();

        if (_suppressAutoFileOpenOnProjectChange)
            return;

        // 外部遷移（Timeline など）で開くべきファイルが指定されている場合はそちらを優先
        if (!string.IsNullOrEmpty(_pendingFileToOpen))
        {
            var target = _pendingFileToOpen;
            _pendingFileToOpen = null;

            if (!string.IsNullOrEmpty(target) && File.Exists(target))
            {
                _ = OpenFileAndSelectNodeAsync(target);
                return;
            }
        }

        // 既に同一プロジェクト配下のファイルを開いている場合は、そのファイルを維持する。
        // (例: 外部遷移でファイルを開いた直後に、LoadProjectsAsync の再選択が走るケース)
        if (!string.IsNullOrEmpty(CurrentFile)
            && File.Exists(CurrentFile)
            && IsPathUnderDirectory(CurrentFile, value.Path))
        {
            _ = OpenFileAndSelectNodeAsync(CurrentFile);
            return;
        }

        // プロジェクト選択時、デフォルトで current_focus.md を開く
        if (!string.IsNullOrEmpty(value.FocusFile) && File.Exists(value.FocusFile))
        {
            _ = OpenFileAndSelectNodeAsync(value.FocusFile);
        }
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsLoading = true;
        try
        {
            var selectedKey = SelectedProject?.HiddenKey;
            var currentFile = CurrentFile;

            var list = await _discoveryService.GetProjectInfoListAsync(force: true);

            Projects.Clear();
            foreach (var p in list)
                Projects.Add(p);

            var nextSelected = selectedKey != null
                ? Projects.FirstOrDefault(p => p.HiddenKey == selectedKey)
                : Projects.FirstOrDefault();

            if (nextSelected != null)
            {
                _suppressAutoFileOpenOnProjectChange = true;
                SelectedProject = nextSelected;
                _suppressAutoFileOpenOnProjectChange = false;

                if (!string.IsNullOrEmpty(currentFile)
                    && File.Exists(currentFile)
                    && IsPathUnderDirectory(currentFile, nextSelected.Path))
                {
                    if (IsDirty)
                    {
                        foreach (var node in TreeNodes)
                        {
                            if (TrySelectNodeRecursive(node, currentFile))
                                break;
                        }
                    }
                    else
                    {
                        await OpenFileAndSelectNodeAsync(currentFile);
                    }
                }
            }
        }
        finally
        {
            _suppressAutoFileOpenOnProjectChange = false;
            IsLoading = false;
        }
    }

    /// <summary>
    /// 外部ページから Editor へ遷移する際に、プロジェクトを切り替えて指定ファイルを開く。
    /// </summary>
    public void NavigateToProjectAndOpenFile(ProjectInfo project, string filePath)
    {
        _pendingFileToOpen = filePath;

        bool projectWillChange = !string.Equals(
            SelectedProject?.HiddenKey,
            project.HiddenKey,
            StringComparison.Ordinal);

        SelectedProject = project;

        // 同一プロジェクト再選択では OnSelectedProjectChanged が発火しないため、ここで直接開く
        if (!projectWillChange)
        {
            _pendingFileToOpen = null;
            _ = OpenFileAndSelectNodeAsync(filePath);
        }
    }

    private static bool IsPathUnderDirectory(string filePath, string directoryPath)
    {
        try
        {
            var fileFull = Path.GetFullPath(filePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var dirFull = Path.GetFullPath(directoryPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return fileFull.StartsWith(dirFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || string.Equals(fileFull, dirFull, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private void BuildFileTree(ProjectInfo project)
    {
        TreeNodes.Clear();

        var aiCtxContent = project.AiContextContentPath;  // _ai-context/context
        var aiCtxObsidianNotes = Path.Combine(project.AiContextPath, "obsidian_notes");

        // 表示するフォルダ定義
        var sections = new[]
        {
            ("_ai-context",           aiCtxContent),
            ("decision_log",          Path.Combine(aiCtxContent, "decision_log")),
            ("focus_history",         Path.Combine(aiCtxContent, "focus_history")),
            ("obsidian_notes",        aiCtxObsidianNotes),
            ("asana (legacy)",        Path.Combine(aiCtxContent, "asana")),
            (".context",              Path.Combine(project.Path, ".context")),
            ("briefings",             Path.Combine(aiCtxContent, "briefings")),
        };

        foreach (var (label, dir) in sections)
        {
            if (!Directory.Exists(dir)) continue;
            
            // decision_log / focus_history / obsidian_notes はデフォルトで閉じる
            bool shouldExpand = label != "decision_log"
                && label != "focus_history"
                && label != "obsidian_notes";
            
            var node = new FileTreeNode { Name = label, FullPath = dir, IsDirectory = true, IsExpanded = shouldExpand };
            AddDirectoryChildren(node, dir, includeSubdirectories: label == "obsidian_notes");
            TreeNodes.Add(node);
        }

        // workstreams セクション
        if (project.HasWorkstreams)
        {
            var workstreamsDir = Path.Combine(aiCtxContent, "workstreams");
            var workstreamsNode = new FileTreeNode
            {
                Name = "workstreams",
                FullPath = workstreamsDir,
                IsDirectory = true,
                IsExpanded = true
            };

            foreach (var ws in project.Workstreams.OrderBy(w => w.IsClosed).ThenBy(w => w.Id))
            {
                var wsLabel = ws.IsClosed ? $"{ws.Label} [closed]" : ws.Label;
                var wsNode = new FileTreeNode
                {
                    Name = wsLabel,
                    FullPath = ws.Path,
                    IsDirectory = true,
                    IsExpanded = !ws.IsClosed,
                    IsClosedWorkstream = ws.IsClosed
                };

                if (ws.FocusFile != null)
                    wsNode.Children.Add(new FileTreeNode { Name = "current_focus.md", FullPath = ws.FocusFile, IsDirectory = false, IsClosedWorkstream = ws.IsClosed });

                var dlDir = Path.Combine(ws.Path, "decision_log");
                if (Directory.Exists(dlDir))
                {
                    var dlNode = new FileTreeNode { Name = "decision_log", FullPath = dlDir, IsDirectory = true, IsExpanded = false, IsClosedWorkstream = ws.IsClosed };
                    AddDirectoryChildren(dlNode, dlDir);
                    wsNode.Children.Add(dlNode);
                }

                var fhDir = Path.Combine(ws.Path, "focus_history");
                if (Directory.Exists(fhDir))
                {
                    var fhNode = new FileTreeNode { Name = "focus_history", FullPath = fhDir, IsDirectory = true, IsExpanded = false, IsClosedWorkstream = ws.IsClosed };
                    AddDirectoryChildren(fhNode, fhDir);
                    wsNode.Children.Add(fhNode);
                }

                workstreamsNode.Children.Add(wsNode);
            }

            TreeNodes.Add(workstreamsNode);
        }

        // ルートの特定ファイル (CLAUDE.md, AGENTS.md)
        var rootFiles = new[] { "CLAUDE.md", "AGENTS.md", "README.md" };
        foreach (var fn in rootFiles)
        {
            var fp = Path.Combine(project.Path, fn);
            if (File.Exists(fp))
                TreeNodes.Add(new FileTreeNode { Name = fn, FullPath = fp, IsDirectory = false });
        }
    }

    private static void AddDirectoryChildren(FileTreeNode parentNode, string dir, bool includeSubdirectories = false)
    {
        try
        {
            if (includeSubdirectories)
            {
                foreach (var subDir in Directory
                    .GetDirectories(dir)
                    .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
                {
                    var directoryNode = new FileTreeNode
                    {
                        Name = Path.GetFileName(subDir),
                        FullPath = subDir,
                        IsDirectory = true,
                        IsExpanded = false
                    };

                    AddDirectoryChildren(directoryNode, subDir, includeSubdirectories: true);
                    parentNode.Children.Add(directoryNode);
                }
            }

            foreach (var f in Directory
                .GetFiles(dir, "*.md")
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(f);
                if (string.Equals(name, "TEMPLATE.md", StringComparison.OrdinalIgnoreCase)) continue;
                parentNode.Children.Add(new FileTreeNode { Name = name, FullPath = f, IsDirectory = false });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Editor] Failed to build tree node for '{dir}': {ex.Message}");
        }
    }

    // =====================================================================
    // ファイルを開き、ツリーのノードを選択状態にする
    // =====================================================================
    public async Task OpenFileAndSelectNodeAsync(string path)
    {
        await OpenFileAsync(path);
        
        // ツリー内の該当ノードを探して選択・展開
        foreach (var node in TreeNodes)
        {
            if (TrySelectNodeRecursive(node, path))
                break;
        }
    }

    private bool TrySelectNodeRecursive(FileTreeNode node, string targetPath)
    {
        if (string.Equals(node.FullPath, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            node.IsSelected = true;
            return true;
        }

        if (node.IsDirectory)
        {
            foreach (var child in node.Children)
            {
                if (TrySelectNodeRecursive(child, targetPath))
                {
                    node.IsExpanded = true;
                    return true;
                }
            }
        }
        return false;
    }

    // =====================================================================
    // ファイルを開く (内部処理)
    // =====================================================================
    public async Task OpenFileAsync(string path)
    {
        IsDiffViewActive = false;

        if (IsDirty)
        {
            var result = MessageBox.Show(
                $"'{Path.GetFileName(CurrentFile)}' has unsaved changes.\nDiscard and continue?",
                "Unsaved Changes",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;
        }

        IsLoading = true;
        try
        {
            var (content, enc) = await _encodingService.ReadFileAsync(path);
            SuppressChangeEvent = true;
            EditorText = content;
            SuppressChangeEvent = false;
            _originalContent = content;
            CurrentFile = path;
            // 同じパスを再オープンした場合 CurrentFile の PropertyChanged が発火しないため強制通知
            OnPropertyChanged(nameof(CurrentFile));
            Encoding = enc;
            IsDirty = false;
            UpdateStatus();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open file:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    // =====================================================================
    // エディタテキスト変更
    // =====================================================================
    public void NotifyTextChanged(string newText)
    {
        if (SuppressChangeEvent) return;
        EditorText = newText;
        IsDirty = newText != _originalContent;
        UpdateStatus();
    }

    // =====================================================================
    // 保存 (Ctrl+S)
    // =====================================================================
    [RelayCommand]
    public async Task SaveAsync()
    {
        if (string.IsNullOrEmpty(CurrentFile)) return;
        try
        {
            await _encodingService.WriteFileAsync(CurrentFile, EditorText, Encoding);

            // current_focus.md 保存時は focus_history スナップショットを作成
            if (Path.GetFileName(CurrentFile).Equals("current_focus.md", StringComparison.OrdinalIgnoreCase))
                await TakeFocusSnapshotAsync();

            _originalContent = EditorText;
            IsDirty = false;
            IsDiffViewActive = false;
            UpdateStatus();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to save:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // =====================================================================
    // focus_history スナップショット
    // =====================================================================
    private async Task TakeFocusSnapshotAsync()
    {
        try
        {
            var dir = Path.GetDirectoryName(CurrentFile)!;
            var histDir = Path.Combine(dir, "focus_history");
            Directory.CreateDirectory(histDir);
            var snapPath = Path.Combine(histDir, $"{DateTime.Now:yyyy-MM-dd}.md");
            await _encodingService.WriteFileAsync(snapPath, EditorText, Encoding);
        }
        catch { }
    }

    // =====================================================================
    // 新規 decision_log 作成
    // =====================================================================
    [RelayCommand]
    public async Task NewDecisionLogAsync()
    {
        if (SelectedProject == null) return;
        if (RequestNewDecisionLogName == null) return;

        var logName = await RequestNewDecisionLogName();
        if (string.IsNullOrWhiteSpace(logName)) return;

        // 現在開いているファイルが Workstream 配下なら、その decision_log に作成する
        var logDir = GetActiveDecisionLogDir();
        Directory.CreateDirectory(logDir);
        var fileName = $"{DateTime.Now:yyyy-MM-dd}_{logName.Trim()}.md";
        var filePath = Path.Combine(logDir, fileName);

        var template = $"# {logName.Trim()}\n\nDate: {DateTime.Now:yyyy-MM-dd}\n\n## Decision\n\n\n## Rationale\n\n\n## Consequences\n\n";
        await _encodingService.WriteFileAsync(filePath, template, "UTF8");

        if (SelectedProject != null)
            BuildFileTree(SelectedProject);

        await OpenFileAndSelectNodeAsync(filePath);
    }

    private string GetActiveDecisionLogDir()
    {
        if (!string.IsNullOrEmpty(CurrentFile))
        {
            var wsPath = DetectWorkstreamPath(CurrentFile);
            if (wsPath != null)
                return Path.Combine(wsPath, "decision_log");
        }
        return Path.Combine(SelectedProject!.AiContextContentPath, "decision_log");
    }

    private string? DetectWorkstreamPath(string filePath)
    {
        if (SelectedProject == null) return null;
        var wsBase = Path.Combine(SelectedProject.AiContextContentPath, "workstreams");
        if (!filePath.StartsWith(wsBase, StringComparison.OrdinalIgnoreCase)) return null;

        var relative = filePath[wsBase.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var sep = relative.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]);
        var wsId = sep < 0 ? relative : relative[..sep];
        return string.IsNullOrEmpty(wsId) ? null : Path.Combine(wsBase, wsId);
    }

    // =====================================================================
    // decision_log 削除 (右クリック)
    // =====================================================================
    [RelayCommand]
    public void DeleteDecisionLog(FileTreeNode? node)
    {
        if (node == null || node.IsDirectory) return;
        if (!node.FullPath.Contains("decision_log")) return;

        var result = MessageBox.Show(
            $"Delete '{node.Name}'?",
            "Delete decision_log",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            File.Delete(node.FullPath);
            if (string.Equals(CurrentFile, node.FullPath, StringComparison.OrdinalIgnoreCase))
            {
                EditorText = "";
                _originalContent = "";
                CurrentFile = "";
                IsDirty = false;
                UpdateStatus();
            }
            if (SelectedProject != null)
                BuildFileTree(SelectedProject);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to delete:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // =====================================================================
    // Diff ビュートグル
    // =====================================================================
    [RelayCommand]
    public void ToggleDiffView() => IsDiffViewActive = !IsDiffViewActive;

    // =====================================================================
    // 検索バートグル
    // =====================================================================
    [RelayCommand]
    public void ToggleSearchBar() => IsSearchBarVisible = !IsSearchBarVisible;

    // =====================================================================
    // Update Focus from Asana
    // =====================================================================
    [RelayCommand(CanExecute = nameof(CanUpdateFocus))]
    public async Task UpdateFocusAsync()
    {
        if (SelectedProject == null) return;

        // API キー未設定チェック
        var settings = _configService.LoadSettings();
        if (string.IsNullOrWhiteSpace(settings.LlmApiKey))
        {
            MessageBox.Show(
                "LLM API key is not configured.\nPlease open Settings and set the API key under \"LLM API\".",
                "Update Focus",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        // Workstream 選択: 1件でも必ずダイアログで確認 (general or workstream)
        string? workstreamId = null;
        var activeWorkstreams = SelectedProject.Workstreams.Where(w => !w.IsClosed).ToList();
        if (activeWorkstreams.Count > 0 && RequestWorkstreamSelection != null)
        {
            var (ok, wsId) = await RequestWorkstreamSelection(activeWorkstreams);
            if (!ok) return; // キャンセル
            workstreamId = wsId; // null = general
        }

        IsLoading = true;
        _focusUpdateCts = new CancellationTokenSource();
        try
        {
            var result = await _focusUpdateService.GenerateProposalAsync(
                SelectedProject, workstreamId, _focusUpdateCts.Token);

            if (RequestFocusUpdateApproval == null) return;
            var capturedResult = result;
            var cts = _focusUpdateCts!;

            // Refine 会話履歴 (instruction → refined result の積み上げ)
            var refineHistory = new List<(string instruction, string result)>();
            var initialUserPrompt = capturedResult.DebugUserPrompt;
            var initialProposed   = capturedResult.ProposedContent;

            var (apply, content) = await RequestFocusUpdateApproval(
                result,
                async (_, instructions) =>
                {
                    var refined = await _focusUpdateService.RefineAsync(
                        initialUserPrompt, initialProposed, instructions, refineHistory, cts.Token);
                    refineHistory.Add((instructions, refined));
                    // Refine 後のデバッグ情報を更新 (View Debug で送信した全会話が見えるようにする)
                    capturedResult.DebugSystemPrompt = _llmClient.LastSystemPrompt;
                    capturedResult.DebugUserPrompt   = BuildRefineDebugConversation(
                        initialUserPrompt, initialProposed, refineHistory);
                    capturedResult.DebugResponse     = refined;
                    return refined;
                });
            if (!apply) return;

            // 適用
            var finalContent = content ?? result.ProposedContent;

            // 更新日付を今日で上書き
            finalContent = UpdateDateLine(finalContent);

            await _encodingService.WriteFileAsync(result.TargetFocusPath, finalContent, "UTF8");

            // focus_history スナップショット
            var histDir = Path.Combine(Path.GetDirectoryName(result.TargetFocusPath)!, "focus_history");
            Directory.CreateDirectory(histDir);
            var snapPath = Path.Combine(histDir, $"{DateTime.Now:yyyy-MM-dd}.md");
            await _encodingService.WriteFileAsync(snapPath, finalContent, "UTF8");

            // Editor で current_focus.md を開く
            if (SelectedProject != null)
                BuildFileTree(SelectedProject);
            await OpenFileAndSelectNodeAsync(result.TargetFocusPath);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (ShowScrollableError != null)
                ShowScrollableError("Update Focus failed", ex.Message);
            else
                MessageBox.Show($"Update Focus failed:\n{ex.Message}", "Update Focus",
                    MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
            _focusUpdateCts?.Dispose();
            _focusUpdateCts = null;
        }
    }

    private bool CanUpdateFocus() => SelectedProject != null;

    private static string BuildRefineDebugConversation(
        string initialUserPrompt,
        string initialProposed,
        IReadOnlyList<(string instruction, string result)> history)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[user — initial prompt]");
        sb.AppendLine(initialUserPrompt);
        sb.AppendLine();
        sb.AppendLine("[assistant — initial proposal]");
        sb.AppendLine(initialProposed);
        foreach (var (instr, res) in history.Take(history.Count - 1))
        {
            sb.AppendLine();
            sb.AppendLine("[user — refine instruction]");
            sb.AppendLine(instr);
            sb.AppendLine();
            sb.AppendLine("[assistant — refined result]");
            sb.AppendLine(res);
        }
        if (history.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("[user — latest refine instruction]");
            sb.AppendLine(history[^1].instruction);
        }
        return sb.ToString();
    }

    private static string UpdateDateLine(string content)
    {
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        var datePattern = new System.Text.RegularExpressions.Regex(
            @"(更新:|Last Updated:)\s*\d{4}-\d{2}-\d{2}",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (datePattern.IsMatch(content))
            return datePattern.Replace(content, m => $"{m.Groups[1].Value} {today}");

        // どちらのパターンもなければ変更しない (LLM が既に処理済みとみなす)
        return content;
    }

    // =====================================================================
    // ステータスバー更新 (メッセンジャー経由)
    // =====================================================================
    private void UpdateStatus()
    {
        var projectLabel = SelectedProject?.Name ?? "";
        var fileLabel = string.IsNullOrEmpty(CurrentFile) ? "" : Path.GetFileName(CurrentFile);

        // メッセンジャー経由で MainWindowViewModel へ通知
        WeakReferenceMessenger.Default.Send(new StatusUpdateMessage(projectLabel, fileLabel, Encoding, IsDirty));
    }
}
