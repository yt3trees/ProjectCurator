using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Rendering;
using AvaloniaEdit.Document;

namespace ProjectCurator.Desktop.Helpers;

public class DiffLineBackgroundRenderer : IBackgroundRenderer
{
    private readonly List<(int line, bool isAdd)> _diffLines = [];

    public KnownLayer Layer => KnownLayer.Background;

    public void SetDiffLines(IEnumerable<(int line, bool isAdd)> lines)
    {
        _diffLines.Clear();
        _diffLines.AddRange(lines);
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (textView.Document == null) return;
        foreach (var (lineNum, isAdd) in _diffLines)
        {
            if (lineNum < 1 || lineNum > textView.Document.LineCount) continue;
            var line = textView.Document.GetLineByNumber(lineNum);
            var segment = BackgroundGeometryBuilder.GetRectsForSegment(textView, line);
            var brush = isAdd
                ? new SolidColorBrush(Color.FromArgb(60, 0, 200, 0))
                : new SolidColorBrush(Color.FromArgb(60, 200, 0, 0));
            foreach (var rect in segment)
                drawingContext.DrawRectangle(brush, null, rect);
        }
    }
}
