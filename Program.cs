using System.Windows;

namespace CopyClaude;

internal static class Program
{
    [STAThread] // requis par WPF
    private static void Main()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };

        var fenetre = new FloatingWindow();
        var listener = new ClipboardListener();

        // Sélection terminal stabilisée → bloc ajouté en fin de fenêtre flottante.
        listener.CaptureStabilisee += fenetre.AjouterBloc;

        // Cleanup à la sortie : désabonnement du listener clipboard.
        app.Exit += (_, _) => listener.Dispose();

        app.Run(fenetre);
    }
}
