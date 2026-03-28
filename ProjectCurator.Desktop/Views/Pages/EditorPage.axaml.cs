using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;
using ProjectCurator.Desktop.Helpers;
using ProjectCurator.ViewModels;
using System.Xml;

namespace ProjectCurator.Desktop.Views.Pages;

public partial class EditorPage : UserControl
{
    private TextEditor? _editor;
    private EditorViewModel? _viewModel;
    private DiffLineBackgroundRenderer? _diffRenderer;
    private bool _suppressTextSync;

    public EditorPage()
    {
        InitializeComponent();
        DataContext = App.Services.GetService(typeof(EditorViewModel));
        _viewModel = DataContext as EditorViewModel;
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        _editor = this.FindControl<TextEditor>("TextEditor");
        if (_editor == null) return;

        // Load Markdown syntax highlighting from embedded resource
        var xshdUri = new Uri("avares://ProjectCurator/Assets/Markdown.xshd");
        using var stream = AssetLoader.Open(xshdUri);
        using var reader = new XmlTextReader(stream);
        _editor.SyntaxHighlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);

        // Register diff background renderer
        _diffRenderer = new DiffLineBackgroundRenderer();
        _editor.TextArea.TextView.BackgroundRenderers.Add(_diffRenderer);

        if (_viewModel != null)
        {
            // Initial text sync
            _suppressTextSync = true;
            _editor.Document.Text = _viewModel.EditorText ?? string.Empty;
            _suppressTextSync = false;

            // ViewModel -> Editor: when EditorText changes externally (file load)
            _viewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(EditorViewModel.EditorText) && !_suppressTextSync)
                {
                    _suppressTextSync = true;
                    if (_editor.Document.Text != _viewModel.EditorText)
                        _editor.Document.Text = _viewModel.EditorText ?? string.Empty;
                    _suppressTextSync = false;
                }
            };

            // Editor -> ViewModel: user edits
            _editor.TextChanged += (_, _) =>
            {
                if (_suppressTextSync) return;
                _suppressTextSync = true;
                if (_viewModel != null)
                    _viewModel.EditorText = _editor.Document.Text;
                _suppressTextSync = false;
            };
        }
    }
}
