using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace CopyClaude;

/// <summary>
/// Fenêtre flottante : toujours au premier plan, ne vole JAMAIS le focus lors
/// d'un ajout automatique (WS_EX_NOACTIVATE), mais activable par un clic manuel
/// pour taper du texte librement.
/// </summary>
internal sealed class FloatingWindow : Window
{
    /// <summary>Police réduite des blocs capturés.</summary>
    private const double CaptureFontSize = 10.5;

    /// <summary>Police normale des notes tapées.</summary>
    private const double NoteFontSize = 13;

    private static readonly Brush CaptureBrush = new SolidColorBrush(Color.FromRgb(0xA0, 0xA8, 0xB8));

    private readonly RichTextBox _editor;
    private readonly ToggleButton _autoFocus;

    public FloatingWindow()
    {
        Title = "Captures terminal";
        Width = 420;
        Height = 320;
        MinWidth = 240;
        MinHeight = 160;
        Topmost = true;              // toujours visible au-dessus du terminal
        ShowActivated = false;       // ne prend pas le focus à l'ouverture
        ShowInTaskbar = true;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        Background = Brushes.Transparent;
        AllowsTransparency = true;

        // Position par défaut : coin bas-droit de la zone de travail.
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 16;
        Top = workArea.Bottom - Height - 16;

        // --- En-tête : zone de drag + boutons ---
        var title = new TextBlock
        {
            Text = "Captures terminal",
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
            FontSize = 12,
        };

        // Toggle : si actif, chaque capture donne le focus à la fenêtre (assumé :
        // c'est la seule exception, volontaire, à la règle « jamais voler le focus »).
        _autoFocus = new ToggleButton
        {
            Content = "Auto-focus",
            ToolTip = "Prendre le focus à chaque capture, prêt à taper la note",
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0xAA)),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 2, 8, 2),
            FontSize = 11,
            Cursor = Cursors.Hand,
        };
        _autoFocus.Checked += (_, _) => _autoFocus.Foreground = Brushes.White;
        _autoFocus.Unchecked += (_, _) => _autoFocus.Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0xAA));

        var clearButton = CreateHeaderButton("Effacer", "Vider le contenu de la fenêtre");
        clearButton.Click += (_, _) => _editor!.Document.Blocks.Clear();

        var quitButton = CreateHeaderButton("✕", "Quitter l'application");
        quitButton.Click += (_, _) => Close();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(_autoFocus);
        buttons.Children.Add(clearButton);
        buttons.Children.Add(quitButton);

        var headerPanel = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(buttons, Dock.Right);
        headerPanel.Children.Add(buttons);
        headerPanel.Children.Add(title);

        var header = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x3A)),
            Height = 28,
            Child = headerPanel,
        };
        // Drag de la fenêtre par l'en-tête.
        header.MouseLeftButtonDown += (_, _) => DragMove();

        // --- Zone de texte unique : blocs capturés (police réduite) + notes libres
        // (police normale). RichTextBox car un TextBox ne sait pas mélanger les polices.
        _editor = new RichTextBox
        {
            AcceptsReturn = true,
            AcceptsTab = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(6),
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = NoteFontSize,
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x28)),
            Foreground = Brushes.Gainsboro,
            CaretBrush = Brushes.White,
        };
        _editor.Document.Blocks.Clear(); // retire le paragraphe vide créé par défaut

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(header, 0);
        Grid.SetRow(_editor, 1);
        grid.Children.Add(header);
        grid.Children.Add(_editor);

        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x28)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x4A, 0x4A, 0x5A)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = grid,
        };

        // Piège n°1 : WS_EX_NOACTIVATE appliqué dès que le HWND existe, pour que
        // la fenêtre ne s'active jamais d'elle-même (les ajouts automatiques ne
        // volent pas le focus du terminal).
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var styles = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE);
            Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, styles | Native.WS_EX_NOACTIVATE);
        };

        // Un clic MANUEL doit, lui, pouvoir activer la fenêtre pour taper :
        // WS_EX_NOACTIVATE bloque l'activation au clic, mais SetForegroundWindow
        // reste autorisé puisque c'est notre process qui vient de recevoir l'input.
        PreviewMouseDown += (_, _) =>
            Native.SetForegroundWindow(new WindowInteropHelper(this).Handle);

        // Clic qui PREND le focus → caret en fin de buffer, prêt à taper sous la
        // dernière capture. Si la zone a déjà le focus, comportement classique
        // (le caret va là où on clique).
        _editor.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (_editor.IsKeyboardFocusWithin)
                return;
            Native.SetForegroundWindow(new WindowInteropHelper(this).Handle);
            _editor.Focus();
            _editor.CaretPosition = _editor.Document.ContentEnd;
            _editor.ScrollToEnd();
            e.Handled = true; // empêche ce premier clic de replacer le caret
        };
    }

    /// <summary>
    /// Ajoute un bloc capturé en FIN de buffer (n'écrase jamais le texte tapé) :
    /// paragraphe en police réduite et grisée, lignes préfixées « &gt; », espacé
    /// de ce qui précède, puis un paragraphe vide en police normale où le caret
    /// se pose pour enchaîner une note. N'active pas la fenêtre.
    /// </summary>
    public void AppendBlock(string text)
    {
        var block = new Paragraph
        {
            FontSize = CaptureFontSize,
            Foreground = CaptureBrush,
            // Marge haute = l'espacement entre le contenu existant et la capture.
            Margin = new Thickness(0, _editor.Document.Blocks.Count > 0 ? 12 : 0, 0, 4),
        };
        var lines = text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
                block.Inlines.Add(new LineBreak());
            block.Inlines.Add(new Run("> " + lines[i]));
        }
        _editor.Document.Blocks.Add(block);

        // Paragraphe vide en police normale : ce que je tape ensuite est en grand.
        var note = new Paragraph
        {
            FontSize = NoteFontSize,
            Foreground = Brushes.Gainsboro,
            Margin = new Thickness(0),
        };
        _editor.Document.Blocks.Add(note);

        _editor.CaretPosition = note.ContentStart;
        _editor.ScrollToEnd();

        // Auto-focus actif → la fenêtre prend le focus, prête pour la note.
        if (_autoFocus.IsChecked == true)
            GrabFocus();
    }

    /// <summary>
    /// Donne le focus à la fenêtre depuis l'arrière-plan : Windows n'honore
    /// SetForegroundWindow que pour le thread qui possède l'input, donc on
    /// s'attache temporairement au thread de la fenêtre au premier plan.
    /// </summary>
    private void GrabFocus()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var foregroundThread = Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out _);
        var ourThread = Native.GetCurrentThreadId();

        var attached = foregroundThread != 0 && foregroundThread != ourThread
            && Native.AttachThreadInput(foregroundThread, ourThread, true);
        try
        {
            Native.SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached)
                Native.AttachThreadInput(foregroundThread, ourThread, false);
        }
        _editor.Focus();
    }

    private static Button CreateHeaderButton(string content, string tooltip) => new()
    {
        Content = content,
        ToolTip = tooltip,
        Background = Brushes.Transparent,
        Foreground = Brushes.White,
        BorderThickness = new Thickness(0),
        Padding = new Thickness(8, 2, 8, 2),
        FontSize = 11,
        Cursor = Cursors.Hand,
    };
}
