namespace Curia.Models;

public class EditorState
{
    public string CurrentFile { get; set; } = "";
    public string OriginalContent { get; set; } = "";
    public bool IsDirty { get; set; }
    public string Encoding { get; set; } = "UTF8";  // UTF8/UTF8BOM/SJIS/UTF16LE/UTF16BE
    public bool SuppressChangeEvent { get; set; }
}
