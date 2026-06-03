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
/// pour taper du texte librement. Un buffer de captures par fenêtre de terminal
/// (clé = HWND) ; l'affichage suit le terminal actif, une barre d'onglets permet
/// de basculer à la main.
/// </summary>
internal sealed class FloatingWindow : Window
{
    /// <summary>Police réduite des blocs capturés.</summary>
    private const double CaptureFontSize = 10.5;

    /// <summary>Police normale des notes tapées.</summary>
    private const double NoteFontSize = 13;

    /// <summary>Longueur max d'une étiquette d'onglet avant troncature.</summary>
    private const int MaxTabLength = 24;

    private static readonly Brush CaptureBrush = new SolidColorBrush(Color.FromRgb(0xA0, 0xA8, 0xB8));
    private static readonly Brush InactiveTabBrush = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0xAA));

    private readonly RichTextBox _editor;
    private readonly ToggleButton _autoFocus;
    private readonly StackPanel _tabBar;
    private readonly Border _tabBarHost;

    /// <summary>Buffers par fenêtre de terminal (HWND → document + onglet). IntPtr.Zero = onglet « Notes ».</summary>
    private readonly Dictionary<IntPtr, TerminalBuffer> _buffers = [];

    /// <summary>Buffer actuellement affiché dans l'éditeur (null tant qu'aucune capture).</summary>
    private TerminalBuffer? _current;

    /// <summary>Un buffer = le document d'un terminal et son onglet dans la barre.</summary>
    private sealed class TerminalBuffer
    {
        public required IntPtr Hwnd { get; init; }
        public required FlowDocument Document { get; init; }
        public required ToggleButton Tab { get; init; }
    }

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
            Foreground = InactiveTabBrush,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 2, 8, 2),
            FontSize = 11,
            Cursor = Cursors.Hand,
        };
        _autoFocus.Checked += (_, _) => _autoFocus.Foreground = Brushes.White;
        _autoFocus.Unchecked += (_, _) => _autoFocus.Foreground = InactiveTabBrush;

        var clearButton = CreateHeaderButton("Effacer", "Vider le buffer affiché");
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

        // --- Barre d'onglets : un onglet par terminal, masquée s'il y en a ≤ 1 ---
        _tabBar = new StackPanel { Orientation = Orientation.Horizontal };
        _tabBarHost = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x31)),
            Child = new ScrollViewer
            {
                Content = _tabBar,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            },
            Visibility = Visibility.Collapsed,
        };

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
        _editor.Document = CreateDocument(); // document de départ (deviendra « Notes » si on y tape)

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(header, 0);
        Grid.SetRow(_tabBarHost, 1);
        Grid.SetRow(_editor, 2);
        grid.Children.Add(header);
        grid.Children.Add(_tabBarHost);
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
    /// Ajoute un bloc capturé en FIN du buffer du terminal source (n'écrase
    /// jamais le texte tapé) : paragraphe en police réduite et grisée, lignes
    /// préfixées « &gt; », puis un paragraphe vide en police normale. N'active
    /// pas la fenêtre (sauf toggle Auto-focus).
    /// </summary>
    public void AppendBlock(string text, IntPtr sourceHwnd)
    {
        SweepClosedBuffers();
        var buffer = GetOrCreateBuffer(sourceHwnd);
        RefreshTitle(buffer);

        var doc = buffer.Document;
        var block = new Paragraph
        {
            FontSize = CaptureFontSize,
            Foreground = CaptureBrush,
            // Marge haute = l'espacement entre le contenu existant et la capture.
            Margin = new Thickness(0, doc.Blocks.Count > 0 ? 12 : 0, 0, 4),
        };
        var lines = text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
                block.Inlines.Add(new LineBreak());
            block.Inlines.Add(new Run("> " + lines[i]));
        }
        doc.Blocks.Add(block);

        // Paragraphe vide en police normale : ce que je tape ensuite est en grand.
        var note = new Paragraph
        {
            FontSize = NoteFontSize,
            Foreground = Brushes.Gainsboro,
            Margin = new Thickness(0),
        };
        doc.Blocks.Add(note);

        // Auto-focus actif → on bascule sur ce buffer et la fenêtre prend le focus.
        if (_autoFocus.IsChecked == true)
        {
            SwitchTo(buffer);
            GrabFocus();
        }
        else if (buffer == _current)
        {
            // Le caret ne peut être placé que dans le document affiché.
            _editor.CaretPosition = note.ContentStart;
            _editor.ScrollToEnd();
        }
    }

    /// <summary>
    /// Suivi du premier plan (branché sur ForegroundWatcher) : Topmost seulement
    /// devant un terminal (ou nous-mêmes), et bascule automatique vers le buffer
    /// du terminal actif.
    /// </summary>
    public void OnForegroundChanged(bool relevant, IntPtr hwnd)
    {
        Topmost = relevant;
        SweepClosedBuffers();
        if (relevant && _buffers.TryGetValue(hwnd, out var buffer))
        {
            RefreshTitle(buffer);
            if (buffer != _current)
                SwitchTo(buffer);
        }
    }

    /// <summary>Affiche le buffer demandé : swap du document, surlignage d'onglet, caret en fin.</summary>
    private void SwitchTo(TerminalBuffer buffer)
    {
        _current = buffer;
        if (!ReferenceEquals(_editor.Document, buffer.Document))
            _editor.Document = buffer.Document;
        foreach (var b in _buffers.Values)
        {
            b.Tab.IsChecked = b == buffer;
            b.Tab.Foreground = b == buffer ? Brushes.White : InactiveTabBrush;
        }
        _editor.CaretPosition = buffer.Document.ContentEnd;
        _editor.ScrollToEnd();
    }

    private TerminalBuffer GetOrCreateBuffer(IntPtr hwnd)
    {
        if (_buffers.TryGetValue(hwnd, out var existing))
            return existing;

        // Première capture : si on a déjà tapé dans le document de départ, il
        // devient l'onglet « Notes » (jamais supprimé) ; sinon il est remplacé.
        if (_buffers.Count == 0 && _current is null && DocumentHasText(_editor.Document))
        {
            var notes = RegisterBuffer(IntPtr.Zero, _editor.Document);
            notes.Tab.Content = "Notes";
            _current = notes;
            notes.Tab.IsChecked = true;
        }

        var buffer = RegisterBuffer(hwnd, CreateDocument());
        if (_current is null)
            SwitchTo(buffer);
        return buffer;
    }

    private TerminalBuffer RegisterBuffer(IntPtr hwnd, FlowDocument document)
    {
        var tab = new ToggleButton
        {
            Content = "Terminal",
            Background = Brushes.Transparent,
            Foreground = InactiveTabBrush,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 1, 8, 1),
            FontSize = 10.5,
            Cursor = Cursors.Hand,
        };
        var buffer = new TerminalBuffer { Hwnd = hwnd, Document = document, Tab = tab };
        tab.Click += (_, _) => SwitchTo(buffer); // bascule manuelle (SwitchTo recale IsChecked)

        _buffers[hwnd] = buffer;
        _tabBar.Children.Add(tab);
        UpdateTabBarVisibility();
        return buffer;
    }

    /// <summary>
    /// Retire les buffers dont la fenêtre de terminal n'existe plus (décision
    /// utilisateur : terminal fermé = notes supprimées). Event-driven : appelé
    /// à chaque capture et changement de premier plan, pas de timer.
    /// </summary>
    private void SweepClosedBuffers()
    {
        foreach (var hwnd in _buffers.Keys.Where(h => h != IntPtr.Zero && !Native.IsWindow(h)).ToList())
        {
            var buffer = _buffers[hwnd];
            _buffers.Remove(hwnd);
            _tabBar.Children.Remove(buffer.Tab);
            if (buffer == _current)
                _current = null;
        }

        // Le buffer affiché a disparu → repli sur le premier restant, sinon vide.
        if (_current is null)
        {
            if (_buffers.Count > 0)
                SwitchTo(_buffers.Values.First());
            else
                _editor.Document = CreateDocument();
        }
        UpdateTabBarVisibility();
    }

    /// <summary>Titre vivant : relu sur la fenêtre du terminal à chaque capture / focus.</summary>
    private static void RefreshTitle(TerminalBuffer buffer)
    {
        if (buffer.Hwnd == IntPtr.Zero)
            return; // l'onglet « Notes » garde son nom

        var title = Native.GetWindowTitle(buffer.Hwnd);
        if (string.IsNullOrWhiteSpace(title))
            title = "Terminal";
        buffer.Tab.ToolTip = title;
        buffer.Tab.Content = title.Length > MaxTabLength ? title[..(MaxTabLength - 1)] + "…" : title;
    }

    private void UpdateTabBarVisibility() =>
        _tabBarHost.Visibility = _buffers.Count > 1 ? Visibility.Visible : Visibility.Collapsed;

    private static FlowDocument CreateDocument() => new() { PagePadding = new Thickness(2) };

    private static bool DocumentHasText(FlowDocument document) =>
        new TextRange(document.ContentStart, document.ContentEnd).Text.Trim().Length > 0;

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
