using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace CopyClaude;

/// <summary>
/// Écoute les changements du presse-papier via une fenêtre *message-only* et
/// <c>AddClipboardFormatListener</c> (event-based, jamais de polling).
/// L'app LIT seulement le presse-papier, elle n'y écrit jamais : la sélection
/// reste donc collable ailleurs et aucune boucle de re-déclenchement n'est possible.
/// </summary>
internal sealed class ClipboardListener : IDisposable
{
    /// <summary>Allowlist par défaut des process terminal (nom sans extension, insensible à la casse).</summary>
    private static readonly string[] AllowlistParDefaut =
        ["WindowsTerminal", "pwsh", "powershell", "conhost", "Code"];

    private readonly HashSet<string> _allowlist;
    private readonly HwndSource _source;
    private readonly DispatcherTimer _debounce;

    /// <summary>Vrai si au moins un event pendant la fenêtre de debounce venait du terminal.</summary>
    private bool _eventVenantDuTerminal;

    /// <summary>Déclenché (sur le thread UI) avec le texte final d'une sélection terminal stabilisée.</summary>
    public event Action<string>? CaptureStabilisee;

    public ClipboardListener()
    {
        _allowlist = ChargerAllowlist();

        // Fenêtre message-only : invisible, ne sert qu'à recevoir WM_CLIPBOARDUPDATE.
        _source = new HwndSource(new HwndSourceParameters("CopyClaudeClipboardListener")
        {
            ParentWindow = Native.HWND_MESSAGE,
        });
        _source.AddHook(WndProc);

        if (!Native.AddClipboardFormatListener(_source.Handle))
            throw new InvalidOperationException("AddClipboardFormatListener a échoué.");

        // Debounce ~300 ms : copyOnSelect émet un event à chaque tick de drag ;
        // on n'ajoute le bloc qu'une fois la sélection stable.
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _debounce.Tick += OnDebounceEcoule;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_CLIPBOARDUPDATE)
        {
            // Filtre vérifié AU MOMENT de l'event : on ne retient que les copies
            // faites pendant que le terminal est au premier plan.
            if (ForegroundEstDansAllowlist())
                _eventVenantDuTerminal = true;

            // Chaque event relance la fenêtre de debounce (drag en cours → on attend).
            _debounce.Stop();
            _debounce.Start();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void OnDebounceEcoule(object? sender, EventArgs e)
    {
        _debounce.Stop();
        if (!_eventVenantDuTerminal)
            return;
        _eventVenantDuTerminal = false;

        var texte = LireTexteClipboard();
        if (!string.IsNullOrEmpty(texte))
            CaptureStabilisee?.Invoke(texte);
    }

    private bool ForegroundEstDansAllowlist()
    {
        var hwnd = Native.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
            return false;

        Native.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0)
            return false;

        try
        {
            using var process = Process.GetProcessById((int)pid);
            return _allowlist.Contains(process.ProcessName);
        }
        catch (ArgumentException)
        {
            // Le process a disparu entre-temps.
            return false;
        }
    }

    /// <summary>
    /// Lecture robuste : le presse-papier peut être verrouillé une fraction de
    /// seconde par l'émetteur → on retente ~5 fois avec ~30 ms de délai.
    /// </summary>
    private static string? LireTexteClipboard()
    {
        for (var essai = 0; essai < 5; essai++)
        {
            try
            {
                return Clipboard.ContainsText() ? Clipboard.GetText() : null;
            }
            catch (COMException)
            {
                Thread.Sleep(30);
            }
        }
        return null;
    }

    /// <summary>
    /// Allowlist configurable : un fichier <c>allowlist.txt</c> (un nom de process
    /// par ligne) à côté de l'exe remplace les défauts s'il est présent.
    /// </summary>
    private static HashSet<string> ChargerAllowlist()
    {
        var noms = AllowlistParDefaut.AsEnumerable();
        var chemin = Path.Combine(AppContext.BaseDirectory, "allowlist.txt");
        if (File.Exists(chemin))
        {
            var lignes = File.ReadAllLines(chemin)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.StartsWith('#'))
                .ToArray();
            if (lignes.Length > 0)
                noms = lignes;
        }
        return new HashSet<string>(noms, StringComparer.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        _debounce.Stop();
        Native.RemoveClipboardFormatListener(_source.Handle);
        _source.Dispose();
    }
}
