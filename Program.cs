using System.Windows;

namespace CopyClaude;

internal static class Program
{
    [STAThread] // requis par WPF
    private static void Main()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };

        var window = new FloatingWindow();
        var listener = new ClipboardListener();
        var watcher = new ForegroundWatcher();

        // Sélection terminal stabilisée → bloc ajouté en fin du buffer du
        // terminal source (identifié par son HWND).
        listener.CaptureStabilized += window.AppendBlock;

        // La fenêtre ne reste au-dessus que devant le terminal (ou elle-même),
        // et son affichage suit le buffer du terminal actif.
        watcher.RelevantForegroundChanged += window.OnForegroundChanged;

        // Cleanup à la sortie : désabonnement du listener clipboard et du hook.
        app.Exit += (_, _) =>
        {
            listener.Dispose();
            watcher.Dispose();
        };

        app.Run(window);
    }
}
