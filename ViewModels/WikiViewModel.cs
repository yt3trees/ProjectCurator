using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using ProjectCurator.Models;
using ProjectCurator.Services;
using ProjectCurator.Views;

namespace ProjectCurator.ViewModels;

public enum WikiTab { Pages, Query, Lint }

// ────────────────────────────────────────────────────
// ページツリー用アイテム
// ────────────────────────────────────────────────────

public partial class WikiTreeItem : ObservableObject
{
    public string Title { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public bool IsCategory { get; set; }
    public ObservableCollection<WikiTreeItem> Children { get; } = [];

    [ObservableProperty] private bool isExpanded = true;
}

// ────────────────────────────────────────────────────
// Lint Issue 表示用
// ────────────────────────────────────────────────────

public class WikiLintIssueViewModel
{
    public string Category { get; set; } = "";
    public string Severity { get; set; } = "";
    public string Description { get; set; } = "";
    public string? PagePath { get; set; }
    public string SeverityColor => Severity switch
    {
        "High"   => "#F38BA8",
        "Medium" => "#FAB387",
        _        => "#A6E3A1"
    };
}

// ────────────────────────────────────────────────────
// WikiViewModel
// ────────────────────────────────────────────────────

public partial class WikiViewModel : ObservableObject
{
    private readonly ProjectDiscoveryService _discovery;
    private readonly ConfigService _config;
    private readonly WikiService _wikiService;
    private readonly WikiIngestService _ingestService;
    private readonly WikiQueryService _queryService;
    private readonly WikiLintService _lintService;
    private readonly LlmClientService _llmClient;

    private CancellationTokenSource? _cts;
    private bool _suppressDirtyTracking;
    private string _loadedPageContent = "";

    // ── プロジェクト選択 ──────────────────────────────
    [ObservableProperty] private ObservableCollection<ProjectInfo> projects = [];
    [ObservableProperty] private ProjectInfo? selectedProject;
    [ObservableProperty] private ObservableCollection<string> domains = [];
    [ObservableProperty] private string? selectedDomain;
    [ObservableProperty] private bool hasWiki;
    [ObservableProperty] private string wikiRoot = "";

    // ── タブ切替 ──────────────────────────────────────
    [ObservableProperty] private WikiTab activeTab = WikiTab.Pages;
    public bool IsPagesTab  => ActiveTab == WikiTab.Pages;
    public bool IsQueryTab  => ActiveTab == WikiTab.Query;
    public bool IsLintTab   => ActiveTab == WikiTab.Lint;

    // ── Pages タブ ───────────────────────────────────
    [ObservableProperty] private ObservableCollection<WikiTreeItem> pageTree = [];
    [ObservableProperty] private WikiTreeItem? selectedTreeItem;
    [ObservableProperty] private string previewContent = "";
    [ObservableProperty] private string selectedPagePath = "";
    [ObservableProperty] private string searchText = "";
    [ObservableProperty] private bool isPageDirty;
    [ObservableProperty] private bool isEditMode;
    public bool IsRenderMode => !IsEditMode;

    // ── Query タブ ───────────────────────────────────
    [ObservableProperty] private string queryText = "";
    [ObservableProperty] private string queryAnswer = "";
    [ObservableProperty] private ObservableCollection<string> queryReferencedPages = [];
    [ObservableProperty] private ObservableCollection<WikiQueryRecord> queryHistory = [];
    [ObservableProperty] private bool isQuerying;
    [ObservableProperty] private bool hasQueryAnswer;
    [ObservableProperty] private WikiQueryRecord? lastQueryRecord;

    // ── Lint タブ ─────────────────────────────────────
    [ObservableProperty] private ObservableCollection<WikiLintIssueViewModel> lintIssues = [];
    [ObservableProperty] private string lintLastRun = "Not run yet";
    [ObservableProperty] private bool isLinting;
    [ObservableProperty] private int lintHighCount;
    [ObservableProperty] private int lintMediumCount;
    [ObservableProperty] private int lintLowCount;

    // ── 共通 ─────────────────────────────────────────
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private bool isAiEnabled;
    [ObservableProperty] private string statusText = "";

    // ── Wiki 初期化 ───────────────────────────────────
    [ObservableProperty] private string newWikiDomain = "";
    [ObservableProperty] private bool isCreatingNewDomain;

    public WikiViewModel(
        ProjectDiscoveryService discovery,
        ConfigService config,
        WikiService wikiService,
        WikiIngestService ingestService,
        WikiQueryService queryService,
        WikiLintService lintService,
        LlmClientService llmClient)
    {
        _discovery    = discovery;
        _config       = config;
        _wikiService  = wikiService;
        _ingestService = ingestService;
        _queryService = queryService;
        _lintService  = lintService;
        _llmClient    = llmClient;

        IsAiEnabled = _config.LoadSettings().AiEnabled;
        WeakReferenceMessenger.Default.Register<AiEnabledChangedMessage>(this,
            (_, msg) => IsAiEnabled = msg.Enabled);
    }

    // ────────────────────────────────────────────────
    // 初期化
    // ────────────────────────────────────────────────

    public async Task InitAsync()
    {
        IsLoading = true;
        try
        {
            var all = await _discovery.GetProjectInfoListAsync(force: false);
            var hidden = _config.LoadHiddenProjects();
            var visible = all.Where(p => !hidden.Contains(p.HiddenKey)).ToList();

            Projects.Clear();
            foreach (var p in visible) Projects.Add(p);
            if (Projects.Count > 0) SelectedProject = Projects[0];
        }
        finally { IsLoading = false; }
    }

    // ────────────────────────────────────────────────
    // プロジェクト変更
    // ────────────────────────────────────────────────

    partial void OnSelectedProjectChanged(ProjectInfo? value)
    {
        if (value == null) { HasWiki = false; WikiRoot = ""; Domains.Clear(); SelectedDomain = null; return; }

        var contextPath = GetContextPath(value);
        if (contextPath == null) { HasWiki = false; WikiRoot = ""; Domains.Clear(); SelectedDomain = null; return; }

        var domainList = WikiService.GetDomains(contextPath);
        Domains.Clear();
        foreach (var d in domainList) Domains.Add(d);

        if (Domains.Count > 0)
        {
            SelectedDomain = Domains[0];
        }
        else
        {
            SelectedDomain = null;
            HasWiki = false;
            WikiRoot = "";
        }
    }

    partial void OnSelectedDomainChanged(string? value)
    {
        if (SelectedProject == null || string.IsNullOrEmpty(value))
        {
            HasWiki = false;
            WikiRoot = "";
            return;
        }

        var contextPath = GetContextPath(SelectedProject);
        if (contextPath == null) return;

        var root = WikiService.GetWikiRoot(contextPath, value);
        WikiRoot = root;
        HasWiki = Directory.Exists(root);

        if (HasWiki)
            _ = LoadPagesAsync();
    }

    // ────────────────────────────────────────────────
    // タブ切替
    // ────────────────────────────────────────────────

    partial void OnActiveTabChanged(WikiTab value)
    {
        OnPropertyChanged(nameof(IsPagesTab));
        OnPropertyChanged(nameof(IsQueryTab));
        OnPropertyChanged(nameof(IsLintTab));
    }

    partial void OnIsEditModeChanged(bool value)
    {
        OnPropertyChanged(nameof(IsRenderMode));
    }

    [RelayCommand] private void SwitchToPages() => ActiveTab = WikiTab.Pages;
    [RelayCommand] private void SwitchToQuery() => ActiveTab = WikiTab.Query;
    [RelayCommand] private void SwitchToLint()  => ActiveTab = WikiTab.Lint;

    [RelayCommand] private void ShowCreateDomain() => IsCreatingNewDomain = true;
    [RelayCommand] private void CancelCreateDomain() => IsCreatingNewDomain = false;

    // ────────────────────────────────────────────────
    // Pages タブ
    // ────────────────────────────────────────────────

    private async Task LoadPagesAsync()
    {
        if (!HasWiki) return;
        IsLoading = true;
        try
        {
            var pages = await Task.Run(() => _wikiService.GetAllPages(WikiRoot));
            BuildPageTree(pages);
        }
        finally { IsLoading = false; }
    }

    private void BuildPageTree(IReadOnlyList<WikiPageItem> pages)
    {
        PageTree.Clear();

        // ルートページ (index.md / log.md)
        var rootItems = pages.Where(p => p.IsRoot).OrderBy(p => p.RelativePath).ToList();
        if (rootItems.Count > 0)
        {
            var rootNode = new WikiTreeItem { Title = "Wiki Files", IsCategory = true, IsExpanded = true };
            foreach (var p in rootItems)
                rootNode.Children.Add(new WikiTreeItem { Title = p.Title, RelativePath = p.RelativePath });
            PageTree.Add(rootNode);
        }

        // カテゴリ別
        var categories = new[] { "sources", "entities", "concepts", "analysis" };
        foreach (var cat in categories)
        {
            var catPages = pages.Where(p => p.Category == cat).OrderBy(p => p.Title).ToList();
            if (catPages.Count == 0) continue;
            var node = new WikiTreeItem { Title = cat, IsCategory = true, IsExpanded = true };
            foreach (var p in catPages)
                node.Children.Add(new WikiTreeItem { Title = p.Title, RelativePath = p.RelativePath });
            PageTree.Add(node);
        }
    }

    partial void OnSelectedTreeItemChanged(WikiTreeItem? value)
    {
        if (value == null || value.IsCategory || string.IsNullOrEmpty(value.RelativePath)) return;
        _ = LoadPreviewAsync(value.RelativePath);
    }

    private async Task LoadPreviewAsync(string relativePath)
    {
        SelectedPagePath = relativePath;
        var page = await Task.Run(() => _wikiService.GetPage(WikiRoot, relativePath));
        _suppressDirtyTracking = true;
        PreviewContent = page?.Content ?? "";
        _loadedPageContent = PreviewContent;
        IsPageDirty = false;
        IsEditMode = false;
        _suppressDirtyTracking = false;
    }

    partial void OnPreviewContentChanged(string value)
    {
        if (_suppressDirtyTracking || string.IsNullOrEmpty(SelectedPagePath))
            return;

        IsPageDirty = !string.Equals(value, _loadedPageContent, StringComparison.Ordinal);
    }

    partial void OnSearchTextChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            _ = LoadPagesAsync();
            return;
        }
        _ = SearchPagesAsync(value);
    }

    private async Task SearchPagesAsync(string query)
    {
        if (!HasWiki) return;
        var pages = await Task.Run(() => _wikiService.GetAllPages(WikiRoot)
            .Where(p => p.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        p.RelativePath.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList());
        BuildPageTree(pages);
    }

    [RelayCommand]
    private async Task NavigateToPage(string pageTitle)
    {
        if (string.IsNullOrEmpty(pageTitle) || !HasWiki) return;

        if (PageTree.Count == 0)
            await LoadPagesAsync();

        WikiTreeItem? found = null;
        foreach (var category in PageTree)
        {
            found = category.Children.FirstOrDefault(c =>
                string.Equals(c.Title, pageTitle, StringComparison.OrdinalIgnoreCase));
            if (found != null) break;
        }
        if (found == null) return;

        ActiveTab = WikiTab.Pages;
        SelectedTreeItem = found;
    }

    [RelayCommand]
    private void OpenPageInEditor()
    {
        if (string.IsNullOrEmpty(SelectedPagePath) || SelectedProject == null) return;
        var full = Path.Combine(WikiRoot, SelectedPagePath.Replace('/', Path.DirectorySeparatorChar));
        OnOpenInEditor?.Invoke(SelectedProject, full);
    }

    [RelayCommand]
    private async Task SaveCurrentPage()
    {
        if (!HasWiki || string.IsNullOrEmpty(SelectedPagePath)) return;

        await _wikiService.SavePage(WikiRoot, SelectedPagePath, PreviewContent);
        _loadedPageContent = PreviewContent;
        IsPageDirty = false;
        StatusText = $"Saved: {SelectedPagePath}";
    }

    [RelayCommand]
    private void SwitchToEditMode() => IsEditMode = true;

    [RelayCommand]
    private void SwitchToRenderMode() => IsEditMode = false;

    public Action<ProjectInfo, string>? OnOpenInEditor { get; set; }

    // ── Ingest ────────────────────────────────────

    [RelayCommand]
    private async Task IngestSource(object? parameter)
    {
        if (!HasWiki || parameter == null) return;

        var files = parameter switch
        {
            IEnumerable<string> e => e.ToList(),
            string s => [s],
            _ => new List<string>()
        };

        if (files.Count == 0) return;

        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        IsLoading = true;
        try
        {
            var progress = new Progress<string>(msg => StatusText = msg);
            int count = 0;

            foreach (var file in files)
            {
                count++;
                StatusText = $"Ingesting ({count}/{files.Count}): {Path.GetFileName(file)}...";

                var result = await _ingestService.GenerateIngestProposal(WikiRoot, file, progress, _cts.Token);
                if (!result.Success)
                {
                    StatusText = $"Error on {Path.GetFileName(file)}: {result.ErrorMessage}";
                    // 1つ失敗しても次へ進む（必要に応じて break しても良い）
                    await Task.Delay(2000); // エラーメッセージを見せるため少し待機
                    continue;
                }

                if (!await ReviewIngestChangesAsync(file, result, _cts.Token))
                {
                    StatusText = $"Skipped: {Path.GetFileName(file)} (no changes saved)";
                    continue;
                }

                await _ingestService.ApplyIngestResult(WikiRoot, result, progress, _cts.Token);
                StatusText = $"Saved: {Path.GetFileName(file)}";
            }

            StatusText = $"Ingest complete. Processed {files.Count} files.";
            await LoadPagesAsync();
        }
        catch (OperationCanceledException)
        {
            StatusText = "Ingest cancelled.";
        }
        finally { IsLoading = false; }
    }

    private async Task<bool> ReviewIngestChangesAsync(string sourceFilePath, IngestResult result, CancellationToken cancellationToken)
    {
        var reviewItems = BuildIngestReviewItems(result);
        if (reviewItems.Count == 0)
            return true;

        var owner = Application.Current?.MainWindow;
        if (owner == null)
            throw new InvalidOperationException("Main window is not available for review dialog.");

        for (int i = 0; i < reviewItems.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = reviewItems[i];
            var proposal = new FileUpdateProposal
            {
                CurrentContent = item.CurrentContent,
                ProposedContent = item.ProposedContent,
                Summary = item.Summary,
                DebugSystemPrompt = result.DebugSystemPrompt,
                DebugUserPrompt = result.DebugUserPrompt,
                DebugResponse = result.DebugResponse
            };

            var indexLabel = $"{i + 1}/{reviewItems.Count}";
            var extraInfo = $"Source: {Path.GetFileName(sourceFilePath)}\nPath: {item.Path}\nType: {(item.IsNew ? "New page" : "Update page")}";
            var dialogTitle = item.IsNew
                ? $"Review New Wiki Page ({indexLabel})"
                : $"Review Updated Wiki Page ({indexLabel})";

            var dialogTask = await owner.Dispatcher.InvokeAsync(() =>
                ProposalReviewDialog.ShowAsync(
                    owner,
                    proposal,
                    dialogTitle,
                    titleIcon: item.IsNew ? "＋" : "⟳",
                    extraInfo: extraInfo));
            var (apply, _) = await dialogTask;

            if (!apply)
                return false;
        }

        return true;
    }

    private List<(string Path, bool IsNew, string CurrentContent, string ProposedContent, string Summary)> BuildIngestReviewItems(IngestResult result)
    {
        var items = new List<(string Path, bool IsNew, string CurrentContent, string ProposedContent, string Summary)>();

        foreach (var p in result.NewPages.Where(p => !string.IsNullOrWhiteSpace(p.Path)))
        {
            var path = NormalizeWikiPath(p.Path);
            var current = ReadWikiFileContent(path);
            var summary = string.IsNullOrWhiteSpace(result.Summary)
                ? $"Create new page: {path}"
                : result.Summary;
            items.Add((path, true, current, p.Content ?? "", summary));
        }

        foreach (var u in result.UpdatedPages.Where(u => !string.IsNullOrWhiteSpace(u.Path)))
        {
            var path = NormalizeWikiPath(u.Path);
            var current = ReadWikiFileContent(path);
            var summary = string.IsNullOrWhiteSpace(result.Summary)
                ? $"Update existing page: {path}"
                : result.Summary;
            items.Add((path, false, current, u.Diff ?? "", summary));
        }

        return items
            .GroupBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last())
            .OrderBy(x => x.IsNew ? 0 : 1)
            .ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private string ReadWikiFileContent(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return "";

        var fullPath = Path.Combine(WikiRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
            return "";

        try
        {
            return File.ReadAllText(fullPath);
        }
        catch
        {
            return "";
        }
    }

    private static string NormalizeWikiPath(string path)
        => path.Replace('\\', '/').Trim();

    // Wiki 初期化
    [RelayCommand]
    private async Task InitializeWiki()
    {
        if (SelectedProject == null) return;
        var contextPath = GetContextPath(SelectedProject);
        if (contextPath == null) { StatusText = "Could not find context path for project."; return; }

        if (string.IsNullOrWhiteSpace(NewWikiDomain)) { StatusText = "Domain name is required."; return; }

        IsLoading = true;
        try
        {
            var name   = SelectedProject.Name;
            var domain = NewWikiDomain.Trim();
            await _wikiService.InitializeWiki(contextPath, name, domain);

            var domainList = WikiService.GetDomains(contextPath);
            Domains.Clear();
            foreach (var d in domainList) Domains.Add(d);
            
            SelectedDomain = domain;
            NewWikiDomain = "";
            IsCreatingNewDomain = false;
            StatusText = $"Wiki domain '{domain}' initialized.";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally { IsLoading = false; }
    }

    // ────────────────────────────────────────────────
    // Query タブ
    // ────────────────────────────────────────────────

    [RelayCommand]
    private async Task RunQuery()
    {
        if (!HasWiki || string.IsNullOrWhiteSpace(QueryText) || !IsAiEnabled) return;

        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        IsQuerying = true;
        HasQueryAnswer = false;
        QueryAnswer = "";
        QueryReferencedPages.Clear();
        try
        {
            var record = await _queryService.Query(WikiRoot, QueryText, _cts.Token);
            QueryAnswer = record.Answer;
            foreach (var p in record.ReferencedPages)
                QueryReferencedPages.Add(p);
            HasQueryAnswer = true;
            LastQueryRecord = record;
            QueryHistory.Insert(0, record);
        }
        catch (Exception ex)
        {
            QueryAnswer = $"Error: {ex.Message}";
            HasQueryAnswer = true;
        }
        finally { IsQuerying = false; }
    }

    [RelayCommand]
    private async Task SaveAnswerAsPage()
    {
        if (LastQueryRecord == null || !HasWiki) return;
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        try
        {
            var path = await _queryService.SaveAnswerAsPage(WikiRoot, LastQueryRecord, _cts.Token);
            StatusText = $"Saved: {path}";
            await LoadPagesAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"Error saving: {ex.Message}";
        }
    }

    // ────────────────────────────────────────────────
    // Lint タブ
    // ────────────────────────────────────────────────

    [RelayCommand]
    private async Task RunLint()
    {
        if (!HasWiki) return;

        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        IsLinting = true;
        LintIssues.Clear();
        StatusText = "Running lint...";
        try
        {
            var progress = new Progress<string>(msg => StatusText = msg);
            var result = await _lintService.RunLint(WikiRoot, IsAiEnabled, progress, _cts.Token);

            LintLastRun = result.RunAt.ToString("yyyy-MM-dd HH:mm");
            LintHighCount   = result.Issues.Count(i => i.Severity == WikiLintSeverity.High);
            LintMediumCount = result.Issues.Count(i => i.Severity == WikiLintSeverity.Medium);
            LintLowCount    = result.Issues.Count(i => i.Severity == WikiLintSeverity.Low);

            foreach (var issue in result.Issues)
            {
                LintIssues.Add(new WikiLintIssueViewModel
                {
                    Category    = issue.Category,
                    Severity    = issue.Severity.ToString(),
                    Description = issue.Description,
                    PagePath    = issue.PagePath
                });
            }

            StatusText = result.IsEmpty ? "Lint complete. No issues found." : $"Lint complete. {result.Issues.Count} issues found.";
        }
        catch (Exception ex)
        {
            StatusText = $"Lint error: {ex.Message}";
        }
        finally { IsLinting = false; }
    }

    // ────────────────────────────────────────────────
    // ヘルパー
    // ────────────────────────────────────────────────

    private static string? GetContextPath(ProjectInfo project)
    {
        // AiContextContentPath を最初に確認 (context/ junction)
        if (!string.IsNullOrEmpty(project.AiContextContentPath) && Directory.Exists(project.AiContextContentPath))
            return project.AiContextContentPath;

        // AiContextPath/_context フォールバック
        if (!string.IsNullOrEmpty(project.AiContextPath))
        {
            var candidate = Path.Combine(project.AiContextPath, "context");
            if (Directory.Exists(candidate)) return candidate;
        }

        // プロジェクトパスから推測
        if (!string.IsNullOrEmpty(project.Path))
        {
            var candidate = Path.Combine(project.Path, "_ai-context", "context");
            if (Directory.Exists(candidate)) return candidate;
        }

        return null;
    }
}
