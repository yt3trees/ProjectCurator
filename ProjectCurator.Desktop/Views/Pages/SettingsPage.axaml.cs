using Avalonia.Controls;
using Avalonia.Interactivity;
using ProjectCurator.Interfaces;
using ProjectCurator.ViewModels;
using System.IO;

namespace ProjectCurator.Desktop.Views.Pages;

public partial class SettingsPage : UserControl
{
    private readonly SettingsViewModel? _viewModel;
    private readonly IShellService? _shellService;
    private readonly IDialogService? _dialogService;
    private readonly ITrayService? _trayService;
    private readonly IHotkeyService? _hotkeyService;
    private TextBox? LlmApiKeyTextBox => this.FindControl<TextBox>("LlmApiKeyBox");
    private TextBox? AsanaTokenTextBox => this.FindControl<TextBox>("AsanaTokenBox");

    public SettingsPage()
    {
        InitializeComponent();
        _viewModel = App.Services.GetService(typeof(SettingsViewModel)) as SettingsViewModel;
        _shellService = App.Services.GetService(typeof(IShellService)) as IShellService;
        _dialogService = App.Services.GetService(typeof(IDialogService)) as IDialogService;
        _trayService = App.Services.GetService(typeof(ITrayService)) as ITrayService;
        _hotkeyService = App.Services.GetService(typeof(IHotkeyService)) as IHotkeyService;
        DataContext = _viewModel;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;
        _viewModel.Load();
        InitAutoRefreshCombo();
        InitProviderCombo();
        if (LlmApiKeyTextBox != null)
            LlmApiKeyTextBox.Text = _viewModel.LlmApiKey ?? "";
        if (AsanaTokenTextBox != null)
            AsanaTokenTextBox.Text = _viewModel.AsanaToken ?? "";
    }

    private void InitAutoRefreshCombo()
    {
        if (_viewModel == null) return;
        foreach (var obj in SettingsAutoRefreshComboBox.Items)
        {
            if (obj is ComboBoxItem item &&
                item.Tag is string tag &&
                tag == _viewModel.AutoRefreshMinutes.ToString())
            {
                SettingsAutoRefreshComboBox.SelectedItem = item;
                return;
            }
        }
        SettingsAutoRefreshComboBox.SelectedIndex = 0;
    }

    private void InitProviderCombo()
    {
        if (_viewModel == null) return;
        foreach (var obj in LlmProviderComboBox.Items)
        {
            if (obj is ComboBoxItem item &&
                item.Tag is string tag &&
                string.Equals(tag, _viewModel.LlmProvider, StringComparison.OrdinalIgnoreCase))
            {
                LlmProviderComboBox.SelectedItem = item;
                return;
            }
        }
    }

    private void OnApplyHotkey(object? sender, RoutedEventArgs e)
    {
        _viewModel?.ApplyHotkeyCommand.Execute(null);
        if (!string.IsNullOrWhiteSpace(_hotkeyService?.HotkeyDisplayText))
            _trayService?.UpdateHotkeyDisplay(_hotkeyService.HotkeyDisplayText);
    }

    private void OnApplyCaptureHotkey(object? sender, RoutedEventArgs e)
        => _viewModel?.ApplyCaptureHotkeyCommand.Execute(null);

    private void OnAutoRefreshChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_viewModel == null) return;
        if (SettingsAutoRefreshComboBox.SelectedItem is ComboBoxItem item &&
            item.Tag is string s &&
            int.TryParse(s, out var minutes))
        {
            _viewModel.AutoRefreshMinutes = minutes;
        }
    }

    private void OnLlmProviderChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_viewModel == null) return;
        if (LlmProviderComboBox.SelectedItem is ComboBoxItem item && item.Tag is string provider)
            _viewModel.LlmProvider = provider;
    }

    private void OnLlmApiKeyChanged(object? sender, TextChangedEventArgs e)
    {
        if (_viewModel == null) return;
        _viewModel.LlmApiKey = (sender as TextBox)?.Text ?? "";
    }

    private void OnAsanaTokenChanged(object? sender, TextChangedEventArgs e)
    {
        if (_viewModel == null) return;
        _viewModel.AsanaToken = (sender as TextBox)?.Text ?? "";
    }

    private void OnOpenLocalRoot(object? sender, RoutedEventArgs e)
    {
        _ = TryOpenFolderAsync(_viewModel?.LocalProjectsRoot, "Local Projects Root");
    }

    private void OnOpenCloudRoot(object? sender, RoutedEventArgs e)
    {
        _ = TryOpenFolderAsync(_viewModel?.CloudSyncRoot, "Cloud Sync Root");
    }

    private void OnOpenObsidianRoot(object? sender, RoutedEventArgs e)
    {
        _ = TryOpenFolderAsync(_viewModel?.ObsidianVaultRoot, "Obsidian Vault Root");
    }

    private void OnOpenAsanaOutput(object? sender, RoutedEventArgs e)
    {
        _ = TryOpenFileAsync(_viewModel?.AsanaOutputFile, "Asana Output File");
    }

    private async Task TryOpenFolderAsync(string? path, string label)
    {
        if (_shellService == null) return;

        if (string.IsNullOrWhiteSpace(path))
        {
            await (_dialogService?.ShowMessageAsync("Open Folder", $"{label} is not configured.") ?? Task.CompletedTask);
            return;
        }

        if (!Directory.Exists(path))
        {
            await (_dialogService?.ShowMessageAsync("Open Folder", $"{label} does not exist:\n{path}") ?? Task.CompletedTask);
            return;
        }

        _shellService.OpenFolder(path);
    }

    private async Task TryOpenFileAsync(string? path, string label)
    {
        if (_shellService == null) return;

        if (string.IsNullOrWhiteSpace(path))
        {
            await (_dialogService?.ShowMessageAsync("Open File", $"{label} is not configured.") ?? Task.CompletedTask);
            return;
        }

        if (!File.Exists(path))
        {
            await (_dialogService?.ShowMessageAsync("Open File", $"{label} does not exist:\n{path}") ?? Task.CompletedTask);
            return;
        }

        _shellService.OpenFile(path);
    }
}
