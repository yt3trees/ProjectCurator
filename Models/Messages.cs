using CommunityToolkit.Mvvm.Messaging.Messages;

namespace ProjectCurator.Models;

/// <summary>
/// ステータスバーの表示内容を更新するためのメッセージ。
/// </summary>
public class StatusUpdateMessage(string project, string file, string encoding, bool isDirty)
{
    public string Project { get; } = project;
    public string File { get; } = file;
    public string Encoding { get; } = encoding;
    public bool IsDirty { get; } = isDirty;
}

/// <summary>
/// AI 機能の有効/無効が変わったときのメッセージ。
/// </summary>
public class AiEnabledChangedMessage(bool enabled)
{
    public bool Enabled { get; } = enabled;
}
