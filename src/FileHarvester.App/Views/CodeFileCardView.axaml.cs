// ============================================================================
// FILE:     src/FileHarvester.App/Views/CodeFileCardView.axaml.cs
// PURPOSE:  Code-behind for a source-code card. Configures the read-only
//           AvaloniaEdit surface: installs TextMate syntax highlighting,
//           matches the editor theme to the app's light/dark variant,
//           picks a grammar from the file extension, and loads content
//           on demand when the card is realized.
// RELATION: Bound to a CodeFileCardViewModel. The editor theme follows
//           Application.RequestedThemeVariant so light and dark both work.
// ============================================================================

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.TextMate;
using FileHarvester.App.ViewModels;
using TextMateSharp.Grammars;

namespace FileHarvester.App.Views;

public partial class CodeFileCardView : UserControl
{
    private TextMate.Installation? _textMateInstallation;
    private RegistryOptions? _registryOptions;
    private string? _loadedForPath;

    public CodeFileCardView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        ActualThemeVariantChanged += OnThemeChanged;
        AttachedToVisualTree += OnAttached;
    }

    private CodeFileCardViewModel? ViewModel => DataContext as CodeFileCardViewModel;

    private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        // The named editor only resolves once the control is attached to
        // the visual tree, so (re)configure and load here as well as on
        // DataContextChanged.
        if (ViewModel is not null)
        {
            ConfigureEditor();
            _ = LoadContentAsync();
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        ConfigureEditor();
        _ = LoadContentAsync();
    }

    private void ConfigureEditor()
    {
        var editor = this.FindControl<TextEditor>("CodeEditor");
        if (editor is null || ViewModel is null)
        {
            return;
        }

        // Install TextMate once per card, themed to the current variant.
        // The install is wrapped so a desktop-only failure (font resource,
        // theme loader) cannot leave the editor half-configured and unable
        // to render the body. Fallback = plain editor; body still visible.
        if (_textMateInstallation is null)
        {
            try
            {
                _registryOptions = new RegistryOptions(CurrentThemeName());
                _textMateInstallation = editor.InstallTextMate(_registryOptions);
            }
            catch
            {
                _textMateInstallation = null;
                _registryOptions = null;
            }
        }

        ApplyTheme(editor);
        SetGrammarForFile(ViewModel.FileName);
    }

    private void SetGrammarForFile(string fileName)
    {
        if (_textMateInstallation is null || _registryOptions is null)
        {
            return;
        }

        var extension = Path.GetExtension(fileName);
        var scope = _registryOptions.GetScopeByExtension(extension);
        if (scope is not null)
        {
            _textMateInstallation.SetGrammar(scope);
        }
        // Unknown extension: leave the grammar unset -> plain text.
    }

    private async System.Threading.Tasks.Task LoadContentAsync()
    {
        var editor = this.FindControl<TextEditor>("CodeEditor");
        var vm = ViewModel;
        if (editor is null || vm is null)
        {
            return;
        }

        // Load content only once per file, on demand.
        if (_loadedForPath == vm.FullPath)
        {
            return;
        }

        await vm.EnsureLoadedAsync().ConfigureAwait(false);

        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            editor.Document = new TextDocument(vm.Text);
            _loadedForPath = vm.FullPath;

            // Force a re-measure and re-paint so the newly assigned document
            // grows the row to its real height and the body renders. Without
            // this, a card whose editor was laid out before Document existed
            // can be painted with zero content height - header visible, body
            // invisible. F-CV-3xx: the reported "code does not display" defect.
            editor.InvalidateMeasure();
            editor.InvalidateVisual();

            // F-CV-3xx-DIAG: temporary diagnostic to identify the "code body
            // does not display" defect on the desktop backend. Writes the
            // editor's runtime state to the app's existing log drawer, so the
            // exact cause (missing text, zero size, invisible colors, hidden)
            // can be read from the running app. Remove after diagnosis.
            try
            {
                var docLen = editor.Document?.Text?.Length ?? -1;
                var taFg = editor.TextArea?.Foreground?.ToString() ?? "null";
                var editorFg = editor.Foreground?.ToString() ?? "null";
                var editorBg = editor.Background?.ToString() ?? "null";
                var bounds = editor.Bounds;
                var isVisible = editor.IsVisible;
                var lineCount = editor.Document?.LineCount ?? -1;
                var fontFamily = editor.FontFamily?.ToString() ?? "null";
                var fontSize = editor.FontSize;
                var textArea = editor.TextArea is null ? "null" : "ok";
                var textView = editor.TextArea?.TextView is null ? "null" : "ok";
                var themeVariant = ActualThemeVariant.ToString();
                var msg = $"[F-CV-3xx-DIAG] path={vm.FullPath} docLen={docLen} lineCount={lineCount} " +
                          $"bounds={bounds.Width:0.0}x{bounds.Height:0.0} isVisible={isVisible} " +
                          $"editorFg={editorFg} editorBg={editorBg} textAreaFg={taFg} " +
                          $"fontFamily={fontFamily} fontSize={fontSize} theme={themeVariant} " +
                          $"textArea={textArea} textView={textView}";
                var diagPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cv-diag.log");
                var diagLine = $"{DateTime.UtcNow:O} {msg}{Environment.NewLine}";
                System.IO.File.AppendAllText(diagPath, diagLine, System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                try { System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cv-diag.log"), $"{DateTime.UtcNow:O} [F-CV-3xx-DIAG] failed: {ex.Message}{Environment.NewLine}", System.Text.Encoding.UTF8); } catch { }
            }
        });
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        var editor = this.FindControl<TextEditor>("CodeEditor");
        if (editor is not null)
        {
            ApplyTheme(editor);
        }
    }

    private void ApplyTheme(TextEditor editor)
    {
        if (_textMateInstallation is null || _registryOptions is null)
        {
            return;
        }

        try
        {
            _textMateInstallation.SetTheme(_registryOptions.LoadTheme(CurrentThemeName()));
        }
        catch
        {
            // If TextMate theme loading fails, fall through to the plain
            // foreground/background pair below so the body is still visible.
        }

        var dark = ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
        var fg = new SolidColorBrush(dark ? Color.Parse("#D4D4D4") : Color.Parse("#1F2328"));
        var lnFg = new SolidColorBrush(dark ? Color.Parse("#6E7681") : Color.Parse("#8C959F"));

        // Set the foreground on both the outer editor AND the inner TextArea,
        // because AvaloniaEdit's text renderer draws via TextArea, and setting
        // only the outer foreground can leave the body painted in a color that
        // matches the surface (F-CV-3xx: "header visible, body invisible").
        editor.Foreground = fg;
        editor.LineNumbersForeground = lnFg;
        if (editor.TextArea is not null)
        {
            editor.TextArea.Foreground = fg;
        }
    }

    private ThemeName CurrentThemeName()
        => ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark
            ? ThemeName.DarkPlus
            : ThemeName.LightPlus;
}