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
    private readonly HwndSource _source;
    private readonly DispatcherTimer _debounce;

    /// <summary>Vrai si au moins un event pendant la fenêtre de debounce venait du terminal.</summary>
    private bool _cameFromTerminal;

    /// <summary>Déclenché (sur le thread UI) avec le texte final d'une sélection terminal stabilisée.</summary>
    public event Action<string>? CaptureStabilized;

    public ClipboardListener()
    {
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
        _debounce.Tick += OnDebounceElapsed;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_CLIPBOARDUPDATE)
        {
            // Filtre vérifié AU MOMENT de l'event : on ne retient que les copies
            // faites pendant que le terminal est au premier plan.
            if (ProcessFilter.IsTerminalProcess(Native.GetForegroundWindow()))
                _cameFromTerminal = true;

            // Chaque event relance la fenêtre de debounce (drag en cours → on attend).
            _debounce.Stop();
            _debounce.Start();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void OnDebounceElapsed(object? sender, EventArgs e)
    {
        _debounce.Stop();
        if (!_cameFromTerminal)
            return;
        _cameFromTerminal = false;

        var text = ReadClipboardText();
        if (!string.IsNullOrEmpty(text))
            CaptureStabilized?.Invoke(text);
    }

    /// <summary>
    /// Lecture robuste : le presse-papier peut être verrouillé une fraction de
    /// seconde par l'émetteur → on retente ~5 fois avec ~30 ms de délai.
    /// </summary>
    private static string? ReadClipboardText()
    {
        for (var attempt = 0; attempt < 5; attempt++)
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

    public void Dispose()
    {
        _debounce.Stop();
        Native.RemoveClipboardFormatListener(_source.Handle);
        _source.Dispose();
    }
}
