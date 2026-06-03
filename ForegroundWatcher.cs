namespace CopyClaude;

/// <summary>
/// Suit les changements de fenêtre au premier plan via
/// <c>SetWinEventHook(EVENT_SYSTEM_FOREGROUND)</c> — event-based, pas de polling.
/// Sert à ne garder la fenêtre flottante au-dessus que devant le terminal.
/// </summary>
internal sealed class ForegroundWatcher : IDisposable
{
    private readonly IntPtr _hook;

    /// <summary>
    /// Référence gardée en champ : sans elle le GC collecterait le delegate
    /// passé au hook natif et le callback planterait.
    /// </summary>
    private readonly Native.WinEventDelegate _callback;

    /// <summary>
    /// Déclenché à chaque changement de premier plan, avec <c>true</c> si la
    /// nouvelle fenêtre est un terminal de l'allowlist ou notre propre fenêtre.
    /// </summary>
    public event Action<bool>? RelevantForegroundChanged;

    public ForegroundWatcher()
    {
        _callback = OnForegroundChange;
        // WINEVENT_OUTOFCONTEXT : le callback arrive sur notre thread UI via sa
        // boucle de messages, pas besoin de marshaling supplémentaire.
        _hook = Native.SetWinEventHook(
            Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _callback, 0, 0, Native.WINEVENT_OUTOFCONTEXT);

        if (_hook == IntPtr.Zero)
            throw new InvalidOperationException("SetWinEventHook a échoué.");
    }

    private void OnForegroundChange(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint idEventThread, uint dwmsEventTime)
    {
        // Notre propre fenêtre compte comme « pertinente » : cliquer dedans pour
        // taper une note ne doit pas la faire passer derrière.
        var relevant = ProcessFilter.IsTerminalProcess(hwnd) || ProcessFilter.IsOwnProcess(hwnd);
        RelevantForegroundChanged?.Invoke(relevant);
    }

    public void Dispose() => Native.UnhookWinEvent(_hook);
}
