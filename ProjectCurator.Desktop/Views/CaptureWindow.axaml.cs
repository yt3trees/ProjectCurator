using System.Diagnostics;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Input;
using ProjectCurator.Models;
using ProjectCurator.Services;

namespace ProjectCurator.Desktop.Views;

public partial class CaptureWindow : Window
{
    private enum Screen { Input, Loading, Confirm, TaskApproval, Complete }

    private readonly CaptureService _captureService;
    private readonly ProjectDiscoveryService _discoveryService;
    private readonly ConfigService _configService;

    private CaptureClassification? _classification;
    private List<ProjectInfo> _projects = [];
    private List<AsanaProjectMeta> _asanaProjects = [];
    private List<AsanaSectionMeta> _asanaSections = [];
    private CancellationTokenSource? _cts;
    private CaptureRouteResult? _lastResult;

    private sealed class ComboItem<T>
    {
        public string Label { get; init; } = "";
        public T Value { get; init; } = default!;
        public override string ToString() => Label;
    }

    public Action<string, string>? OnNavigateToFile { get; set; }
    public Action<string, string, string>? OnNavigateToFocusUpdate { get; set; }
    public Action<string, string>? OnNavigateToDecision { get; set; }

    public CaptureWindow()
    {
        InitializeComponent();

        _captureService = App.Services.GetService(typeof(CaptureService)) as CaptureService
            ?? throw new InvalidOperationException("CaptureService is not available.");
        _discoveryService = App.Services.GetService(typeof(ProjectDiscoveryService)) as ProjectDiscoveryService
            ?? throw new InvalidOperationException("ProjectDiscoveryService is not available.");
        _configService = App.Services.GetService(typeof(ConfigService)) as ConfigService
            ?? throw new InvalidOperationException("ConfigService is not available.");

        Loaded += OnLoaded;
        KeyDown += OnKeyDown;

        CategoryCombo.ItemsSource = new[] { "task", "tension", "focus_update", "decision", "memo" };
        CategoryCombo.SelectedIndex = 0;
        ConfirmCategoryCombo.ItemsSource = new[] { "task", "tension", "focus_update", "decision", "memo" };
        ConfirmCategoryCombo.SelectionChanged += OnConfirmCategoryChanged;
        ConfirmProjectCombo.SelectionChanged += OnConfirmProjectChanged;
        AsanaProjectCombo.SelectionChanged += OnAsanaProjectChanged;

        for (var h = 0; h < 24; h++) HourCombo.Items.Add(h.ToString("00"));
        HourCombo.SelectedIndex = 9;
        foreach (var m in new[] { "00", "15", "30", "45" }) MinuteCombo.Items.Add(m);
        MinuteCombo.SelectedIndex = 0;

        CancelBtn.Click += (_, _) => Close();
        CloseBtn.Click += (_, _) => Close();
        CaptureBtn.Click += OnCaptureClick;
        BackToInputBtn.Click += (_, _) => ShowScreen(Screen.Input);
        RouteBtn.Click += OnRouteClick;
        BackToConfirmBtn.Click += (_, _) => ShowScreen(Screen.Confirm);
        ApproveBtn.Click += OnApproveClick;
        SaveAsMemoBtn.Click += OnSaveAsMemoClick;
        OpenFileBtn.Click += OnOpenFileClick;
        OpenAsanaBtn.Click += OnOpenAsanaClick;
    }

    private async void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var settings = _configService.LoadSettings();
        CategoryCombo.IsVisible = !settings.AiEnabled;

        _projects = await _discoveryService.GetProjectInfoListAsync();
        PopulateProjectCombos();
        ShowScreen(Screen.Input);
        InputBox.Focus();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_cts != null)
                _cts.Cancel();
            else
                Close();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.Control)
        {
            if (InputPanel.IsVisible)
                OnCaptureClick(null, null!);
            else if (ConfirmPanel.IsVisible)
                OnRouteClick(null, null!);
            else if (TaskApprovalPanel.IsVisible)
                OnApproveClick(null, null!);
            e.Handled = true;
        }
    }

    private async void OnCaptureClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var input = InputBox.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(input))
            return;

        var selectedProject = (ProjectCombo.SelectedItem as ComboItem<ProjectInfo?>)?.Value?.Name;
        var settings = _configService.LoadSettings();

        if (!settings.AiEnabled)
        {
            var manualCategory = CategoryCombo.SelectedItem as string ?? "memo";
            _classification = _captureService.BuildManualClassification(input, manualCategory, selectedProject ?? "");
            ShowConfirmScreen(input);
            return;
        }

        ShowScreen(Screen.Loading);
        _cts = new CancellationTokenSource();
        try
        {
            _classification = await _captureService.ClassifyAsync(input, selectedProject, _cts.Token);
            ShowConfirmScreen(input);
        }
        catch (OperationCanceledException)
        {
            ShowScreen(Screen.Input);
        }
        catch (Exception ex)
        {
            _classification = _captureService.BuildManualClassification(input, "memo", selectedProject ?? "");
            ShowConfirmScreen(input);
            SetConfirmError($"AI classification failed ({ex.Message}). Manual mode active.");
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    private async void OnRouteClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_classification == null)
            return;

        _classification.Category = ConfirmCategoryCombo.SelectedItem as string ?? _classification.Category;
        _classification.ProjectName = (ConfirmProjectCombo.SelectedItem as ComboItem<ProjectInfo?>)?.Value?.Name ?? "";
        _classification.Summary = ConfirmSummaryBox.Text?.Trim() ?? _classification.Summary;
        SetConfirmError("");

        if (string.IsNullOrWhiteSpace(_classification.ProjectName) && _classification.Category != "memo")
        {
            SetConfirmError("Please select a project.");
            return;
        }

        if (_classification.Category == "task")
        {
            await ShowTaskApprovalScreenAsync();
            return;
        }

        ShowScreen(Screen.Loading);
        _cts = new CancellationTokenSource();
        try
        {
            var result = await _captureService.RouteAsync(_classification, InputBox.Text ?? "", _cts.Token);
            HandleRouteResult(result);
        }
        catch (Exception ex)
        {
            ShowScreen(Screen.Confirm);
            SetConfirmError($"Routing failed: {ex.Message}");
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    private Task ShowTaskApprovalScreenAsync()
    {
        if (_classification == null)
            return Task.CompletedTask;

        var projectMeta = (AsanaProjectCombo.SelectedItem as ComboItem<AsanaProjectMeta?>)?.Value;
        if (projectMeta == null || string.IsNullOrWhiteSpace(projectMeta.Gid))
        {
            SetConfirmError("Please select an Asana project.");
            return Task.CompletedTask;
        }

        var sectionMeta = (AsanaSectionCombo.SelectedItem as ComboItem<AsanaSectionMeta?>)?.Value;
        var dueDate = DueDatePicker.SelectedDate;
        var hasTime = SetTimeCheckBox.IsChecked == true;
        if (hasTime && !dueDate.HasValue)
        {
            SetConfirmError("Please select a due date when setting a time.");
            return Task.CompletedTask;
        }

        var dueOn = "";
        var dueAt = "";
        if (dueDate.HasValue)
        {
            var dt = dueDate.Value.DateTime;
            if (hasTime)
            {
                var hour = int.Parse(HourCombo.SelectedItem?.ToString() ?? "09");
                var minute = int.Parse(MinuteCombo.SelectedItem?.ToString() ?? "00");
                var dto = new DateTimeOffset(dt.Year, dt.Month, dt.Day, hour, minute, 0, DateTimeOffset.Now.Offset);
                dueAt = dto.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz");
            }
            else
            {
                dueOn = dt.ToString("yyyy-MM-dd");
            }
        }

        var preview = new AsanaTaskCreatePreview
        {
            ProjectName = projectMeta.Name,
            ProjectGid = projectMeta.Gid,
            SectionName = sectionMeta?.Name ?? "",
            SectionGid = sectionMeta?.Gid ?? "",
            TaskName = _classification.Summary,
            Notes = _classification.Body,
            DueOn = dueOn,
            DueAt = dueAt,
        };

        var requestData = new Dictionary<string, object>
        {
            ["name"] = preview.TaskName,
            ["notes"] = preview.Notes,
            ["projects"] = new[] { preview.ProjectGid }
        };
        if (!string.IsNullOrWhiteSpace(preview.DueAt))
            requestData["due_at"] = preview.DueAt;
        else if (!string.IsNullOrWhiteSpace(preview.DueOn))
            requestData["due_on"] = preview.DueOn;
        if (!string.IsNullOrWhiteSpace(preview.SectionGid))
        {
            requestData["memberships"] = new[]
            {
                new Dictionary<string, string>
                {
                    ["project"] = preview.ProjectGid,
                    ["section"] = preview.SectionGid
                }
            };
        }

        preview.RequestJson = JsonSerializer.Serialize(new { data = requestData }, new JsonSerializerOptions { WriteIndented = true });
        RequestPreviewBox.Text = $"POST https://app.asana.com/api/1.0/tasks{Environment.NewLine}{Environment.NewLine}{preview.RequestJson}";
        ApproveBtn.Tag = preview;
        TaskApprovalErrorText.IsVisible = false;
        SaveAsMemoBtn.IsVisible = false;
        ShowScreen(Screen.TaskApproval);
        return Task.CompletedTask;
    }

    private async void OnApproveClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (ApproveBtn.Tag is not AsanaTaskCreatePreview preview)
            return;

        ApproveBtn.IsEnabled = false;
        _cts = new CancellationTokenSource();
        try
        {
            var idempotency = CaptureService.BuildIdempotencyKey(preview.ProjectGid, preview.TaskName, preview.Notes);
            var result = await _captureService.CreateAsanaTaskAsync(preview, idempotency, _cts.Token);
            if (!result.Success)
            {
                TaskApprovalErrorText.Text = result.Message;
                TaskApprovalErrorText.IsVisible = true;
                SaveAsMemoBtn.IsVisible = true;
                return;
            }

            HandleRouteResult(result);
        }
        catch (Exception ex)
        {
            TaskApprovalErrorText.Text = ex.Message;
            TaskApprovalErrorText.IsVisible = true;
            SaveAsMemoBtn.IsVisible = true;
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            ApproveBtn.IsEnabled = true;
        }
    }

    private async void OnSaveAsMemoClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_classification == null)
            return;

        _cts = new CancellationTokenSource();
        try
        {
            var memo = new CaptureClassification
            {
                Category = "memo",
                Summary = _classification.Summary,
                Body = _classification.Body,
            };
            var result = await _captureService.RouteAsync(memo, InputBox.Text ?? "", _cts.Token);
            HandleRouteResult(result);
        }
        catch (Exception ex)
        {
            TaskApprovalErrorText.Text = ex.Message;
            TaskApprovalErrorText.IsVisible = true;
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void OnOpenFileClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_lastResult?.TargetFilePath == null || _classification?.ProjectName == null)
            return;

        OnNavigateToFile?.Invoke(_classification.ProjectName, _lastResult.TargetFilePath);
        Close();
    }

    private void OnOpenAsanaClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_lastResult?.AsanaTaskUrl))
        {
            try
            {
                Process.Start(new ProcessStartInfo(_lastResult.AsanaTaskUrl) { UseShellExecute = true });
            }
            catch
            {
                // ignore
            }
        }
    }

    private void OnConfirmCategoryChanged(object? sender, SelectionChangedEventArgs e)
    {
        var category = ConfirmCategoryCombo.SelectedItem as string ?? "memo";
        var isTask = category == "task";
        AsanaProjectLabel.IsVisible = isTask;
        AsanaProjectCombo.IsVisible = isTask;
        AsanaSectionLabel.IsVisible = isTask;
        AsanaSectionCombo.IsVisible = isTask;
        DueDateLabel.IsVisible = isTask;
        DueDateRow.IsVisible = isTask;
        if (isTask)
            _ = LoadAsanaProjectsForCurrentProjectAsync();
    }

    private async void OnConfirmProjectChanged(object? sender, SelectionChangedEventArgs e)
    {
        var category = ConfirmCategoryCombo.SelectedItem as string ?? "memo";
        if (category == "task")
            await LoadAsanaProjectsForCurrentProjectAsync();
    }

    private async void OnAsanaProjectChanged(object? sender, SelectionChangedEventArgs e)
    {
        var project = (AsanaProjectCombo.SelectedItem as ComboItem<AsanaProjectMeta?>)?.Value;
        if (project == null || string.IsNullOrWhiteSpace(project.Gid))
            return;

        _asanaSections = await _captureService.FetchSectionsAsync(project.Gid);
        PopulateAsanaSectionCombo(_classification?.AsanaSectionCandidateGid ?? "");
    }

    private void ShowScreen(Screen screen)
    {
        InputPanel.IsVisible = screen == Screen.Input;
        LoadingPanel.IsVisible = screen == Screen.Loading;
        ConfirmPanel.IsVisible = screen == Screen.Confirm;
        TaskApprovalPanel.IsVisible = screen == Screen.TaskApproval;
        CompletePanel.IsVisible = screen == Screen.Complete;
    }

    private void ShowConfirmScreen(string originalInput)
    {
        if (_classification == null)
            return;

        ConfirmInputPreview.Text = originalInput.Length > 140 ? $"{originalInput[..140]}..." : originalInput;
        ConfirmCategoryCombo.SelectedItem = _classification.Category;
        ConfirmSummaryBox.Text = _classification.Summary;

        var projectItem = ((IEnumerable<object>)ConfirmProjectCombo.ItemsSource!)
            .OfType<ComboItem<ProjectInfo?>>()
            .FirstOrDefault(i => string.Equals(i.Value?.Name, _classification.ProjectName, StringComparison.OrdinalIgnoreCase));
        ConfirmProjectCombo.SelectedItem = projectItem;

        var isTask = _classification.Category == "task";
        AsanaProjectLabel.IsVisible = isTask;
        AsanaProjectCombo.IsVisible = isTask;
        AsanaSectionLabel.IsVisible = isTask;
        AsanaSectionCombo.IsVisible = isTask;
        DueDateLabel.IsVisible = isTask;
        DueDateRow.IsVisible = isTask;

        if (isTask && DateTime.TryParse(_classification.DueOn, out var dueOn))
            DueDatePicker.SelectedDate = new DateTimeOffset(dueOn);
        else
            DueDatePicker.SelectedDate = null;
        SetTimeCheckBox.IsChecked = false;
        SetConfirmError("");

        ShowScreen(Screen.Confirm);
        if (isTask)
            _ = LoadAsanaProjectsForCurrentProjectAsync();
    }

    private void ShowCompleteScreen(CaptureRouteResult result)
    {
        _lastResult = result;
        CompleteMessageText.Text = result.Success ? $"OK: {result.Message}" : $"Error: {result.Message}";
        CompleteMessageText.Foreground = result.Success ? Avalonia.Media.Brushes.LightGreen : Avalonia.Media.Brushes.OrangeRed;
        OpenFileBtn.IsVisible = !string.IsNullOrWhiteSpace(result.TargetFilePath);
        OpenAsanaBtn.IsVisible = !string.IsNullOrWhiteSpace(result.AsanaTaskUrl);
        ShowScreen(Screen.Complete);

        if (result.RequiresNavigation && !string.IsNullOrWhiteSpace(result.NavigationProjectName))
        {
            var captured = _classification?.Body ?? (InputBox.Text ?? "");
            if (_classification?.Category == "focus_update" && result.NavigationFilePath != null)
                OnNavigateToFocusUpdate?.Invoke(result.NavigationProjectName, result.NavigationFilePath, captured);
            else if (_classification?.Category == "decision")
                OnNavigateToDecision?.Invoke(result.NavigationProjectName, captured);
            else if (result.NavigationFilePath != null)
                OnNavigateToFile?.Invoke(result.NavigationProjectName, result.NavigationFilePath);
            Close();
        }
    }

    private void HandleRouteResult(CaptureRouteResult result)
    {
        ShowCompleteScreen(result);
    }

    private void PopulateProjectCombos()
    {
        var inputItems = new List<ComboItem<ProjectInfo?>>
        {
            new() { Label = "Auto-detect", Value = null }
        };
        inputItems.AddRange(_projects.Select(p => new ComboItem<ProjectInfo?> { Label = p.DisplayName, Value = p }));
        ProjectCombo.ItemsSource = inputItems;
        ProjectCombo.SelectedIndex = 0;

        var confirmItems = new List<ComboItem<ProjectInfo?>>
        {
            new() { Label = "(not specified)", Value = null }
        };
        confirmItems.AddRange(_projects.Select(p => new ComboItem<ProjectInfo?> { Label = p.DisplayName, Value = p }));
        ConfirmProjectCombo.ItemsSource = confirmItems;
        ConfirmProjectCombo.SelectedIndex = 0;
    }

    private async Task LoadAsanaProjectsForCurrentProjectAsync()
    {
        var selectedProject = (ConfirmProjectCombo.SelectedItem as ComboItem<ProjectInfo?>)?.Value;
        if (selectedProject == null)
        {
            AsanaProjectCombo.ItemsSource = new[] { new ComboItem<AsanaProjectMeta?> { Label = "(select project first)", Value = null } };
            AsanaProjectCombo.SelectedIndex = 0;
            return;
        }

        var (gids, wsMap) = _captureService.LoadAsanaProjectGids(selectedProject);
        if (gids.Count == 0)
        {
            AsanaProjectCombo.ItemsSource = new[] { new ComboItem<AsanaProjectMeta?> { Label = "No Asana config", Value = null } };
            AsanaProjectCombo.SelectedIndex = 0;
            SetConfirmError("Asana project not configured. Check Asana Sync settings.");
            return;
        }

        string? resolvedGid = null;
        if (!string.IsNullOrWhiteSpace(_classification?.WorkstreamHint) &&
            wsMap.TryGetValue(_classification.WorkstreamHint, out var wsGid))
            resolvedGid = wsGid;
        if (resolvedGid == null && !string.IsNullOrWhiteSpace(_classification?.AsanaProjectCandidateGid) &&
            gids.Contains(_classification.AsanaProjectCandidateGid))
            resolvedGid = _classification.AsanaProjectCandidateGid;

        _asanaProjects = [];
        var items = new List<ComboItem<AsanaProjectMeta?>>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        foreach (var gid in gids)
        {
            var meta = await _captureService.FetchProjectMetaAsync(gid, cts.Token) ?? new AsanaProjectMeta { Gid = gid, Name = gid };
            _asanaProjects.Add(meta);
            items.Add(new ComboItem<AsanaProjectMeta?> { Label = meta.DisplayLabel, Value = meta });
        }
        AsanaProjectCombo.ItemsSource = items;
        if (resolvedGid != null)
        {
            var target = items.FirstOrDefault(i => i.Value?.Gid == resolvedGid);
            AsanaProjectCombo.SelectedItem = target ?? items.FirstOrDefault();
        }
        else
        {
            AsanaProjectCombo.SelectedIndex = items.Count == 1 ? 0 : -1;
        }
    }

    private void PopulateAsanaSectionCombo(string candidateGid)
    {
        var items = new List<ComboItem<AsanaSectionMeta?>>
        {
            new() { Label = "(none)", Value = null }
        };
        items.AddRange(_asanaSections.Select(s => new ComboItem<AsanaSectionMeta?> { Label = s.DisplayLabel, Value = s }));
        AsanaSectionCombo.ItemsSource = items;
        AsanaSectionCombo.SelectedIndex = 0;
        if (!string.IsNullOrWhiteSpace(candidateGid))
        {
            var target = items.FirstOrDefault(i => i.Value?.Gid == candidateGid);
            if (target != null)
                AsanaSectionCombo.SelectedItem = target;
        }
    }

    private void SetConfirmError(string message)
    {
        ConfirmErrorText.Text = message;
        ConfirmErrorText.IsVisible = !string.IsNullOrWhiteSpace(message);
    }
}
