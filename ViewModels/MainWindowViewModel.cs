using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Curia.Models;

namespace Curia.ViewModels;

public partial class MainWindowViewModel : ObservableObject, IRecipient<StatusUpdateMessage>
{
    // ---- ステータスバーの状態 ----
    [ObservableProperty]
    private string statusProject = "";

    [ObservableProperty]
    private string statusFile = "";

    [ObservableProperty]
    private string statusEncoding = "";

    [ObservableProperty]
    private bool statusDirty = false;

    [ObservableProperty]
    private bool isEditorActive = false;

    public CommandPaletteViewModel CommandPaletteViewModel { get; }

    public MainWindowViewModel(CommandPaletteViewModel commandPaletteViewModel)
    {
        CommandPaletteViewModel = commandPaletteViewModel;
        // メッセージの受信登録
        WeakReferenceMessenger.Default.Register(this);
    }

    // ステータス更新メッセージを受信
    public void Receive(StatusUpdateMessage message)
    {
        StatusProject = message.Project;
        StatusFile = message.File;
        StatusEncoding = message.Encoding;
        StatusDirty = message.IsDirty;
    }
}
