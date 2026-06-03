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
        var watcher = new ForegroundWatcher();

        // Sélection terminal stabilisée → bloc ajouté en fin de fenêtre flottante.
        listener.CaptureStabilisee += fenetre.AjouterBloc;

        // La fenêtre ne reste au-dessus que devant le terminal (ou elle-même) ;
        // ailleurs elle perd Topmost et se laisse recouvrir.
        watcher.PremierPlanPertinent += pertinent => fenetre.Topmost = pertinent;

        // Cleanup à la sortie : désabonnement du listener clipboard et du hook.
        app.Exit += (_, _) =>
        {
            listener.Dispose();
            watcher.Dispose();
        };

        app.Run(fenetre);
    }
}
