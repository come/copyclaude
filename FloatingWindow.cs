using System.Text;
using System.Windows;
using System.Windows.Controls;
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
    private readonly TextBox _zone;

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
        var zone = SystemParameters.WorkArea;
        Left = zone.Right - Width - 16;
        Top = zone.Bottom - Height - 16;

        // --- En-tête : zone de drag + boutons ---
        var titre = new TextBlock
        {
            Text = "Captures terminal",
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
            FontSize = 12,
        };

        var boutonEffacer = CreerBoutonEntete("Effacer", "Vider le contenu de la fenêtre");
        boutonEffacer.Click += (_, _) => _zone!.Clear();

        var boutonQuitter = CreerBoutonEntete("✕", "Quitter l'application");
        boutonQuitter.Click += (_, _) => Close();

        var boutons = new StackPanel { Orientation = Orientation.Horizontal };
        boutons.Children.Add(boutonEffacer);
        boutons.Children.Add(boutonQuitter);

        var grilleEntete = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(boutons, Dock.Right);
        grilleEntete.Children.Add(boutons);
        grilleEntete.Children.Add(titre);

        var entete = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x3A)),
            Height = 28,
            Child = grilleEntete,
        };
        // Drag de la fenêtre par l'en-tête.
        entete.MouseLeftButtonDown += (_, _) => DragMove();

        // --- Zone de texte unique : blocs capturés + notes libres ---
        _zone = new TextBox
        {
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(6),
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 12,
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x28)),
            Foreground = Brushes.Gainsboro,
            CaretBrush = Brushes.White,
        };

        var grille = new Grid();
        grille.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grille.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(entete, 0);
        Grid.SetRow(_zone, 1);
        grille.Children.Add(entete);
        grille.Children.Add(_zone);

        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x28)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x4A, 0x4A, 0x5A)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = grille,
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
    }

    /// <summary>
    /// Ajoute un bloc capturé en FIN de buffer (n'écrase jamais le texte tapé) :
    /// chaque ligne préfixée « &gt; », puis une ligne vide, caret en fin pour
    /// enchaîner une note sans cliquer. N'active pas la fenêtre.
    /// </summary>
    public void AjouterBloc(string texte)
    {
        var sb = new StringBuilder(_zone.Text);
        if (sb.Length > 0 && !_zone.Text.EndsWith('\n'))
            sb.AppendLine();

        foreach (var ligne in texte.Replace("\r\n", "\n").TrimEnd('\n').Split('\n'))
            sb.Append("> ").AppendLine(ligne);
        sb.AppendLine(); // ligne vide où poser le caret pour la note

        _zone.Text = sb.ToString();
        _zone.CaretIndex = _zone.Text.Length;
        _zone.ScrollToEnd();
    }

    private static Button CreerBoutonEntete(string contenu, string infobulle) => new()
    {
        Content = contenu,
        ToolTip = infobulle,
        Background = Brushes.Transparent,
        Foreground = Brushes.White,
        BorderThickness = new Thickness(0),
        Padding = new Thickness(8, 2, 8, 2),
        FontSize = 11,
        Cursor = Cursors.Hand,
    };
}
