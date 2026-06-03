using System.Runtime.InteropServices;

namespace CopyClaude;

/// <summary>
/// Tous les P/Invoke user32 du projet, regroupés ici (convention CLAUDE.md).
/// </summary>
internal static class Native
{
    // --- Constantes Win32 ---

    /// <summary>0x031D : message envoyé aux listeners quand le contenu du presse-papier change.</summary>
    public const int WM_CLIPBOARDUPDATE = 0x031D;

    /// <summary>-20 : index GetWindowLong/SetWindowLong pour lire/écrire le style étendu.</summary>
    public const int GWL_EXSTYLE = -20;

    /// <summary>0x08000000 : style étendu — la fenêtre ne s'active pas au clic (ne vole jamais le focus).</summary>
    public const int WS_EX_NOACTIVATE = 0x08000000;

    /// <summary>-3 : parent spécial pour créer une fenêtre *message-only* (invisible, reçoit uniquement des messages).</summary>
    public static readonly IntPtr HWND_MESSAGE = new(-3);

    /// <summary>0x0003 : événement système — la fenêtre au premier plan a changé.</summary>
    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;

    /// <summary>0x0000 : le callback du hook s'exécute dans notre process (pas de DLL injectée).</summary>
    public const uint WINEVENT_OUTOFCONTEXT = 0x0000;

    // --- Écoute du presse-papier (event-based, jamais de polling) ---

    /// <summary>Abonne la fenêtre aux notifications WM_CLIPBOARDUPDATE.</summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AddClipboardFormatListener(IntPtr hwnd);

    /// <summary>Désabonne la fenêtre — à appeler impérativement à la fermeture.</summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    // --- Filtre fenêtre active ---

    /// <summary>Handle de la fenêtre actuellement au premier plan.</summary>
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    /// <summary>PID du process propriétaire d'une fenêtre (retour = thread id, ignoré ici).</summary>
    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    // --- Suivi des changements de fenêtre au premier plan (event-based) ---

    /// <summary>Signature du callback de SetWinEventHook.</summary>
    public delegate void WinEventDelegate(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint idEventThread, uint dwmsEventTime);

    /// <summary>Installe un hook sur une plage d'événements système (ici EVENT_SYSTEM_FOREGROUND).</summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWinEventHook(
        uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate pfnWinEventProc,
        uint idProcess, uint idThread, uint dwFlags);

    /// <summary>Retire le hook — à appeler impérativement à la fermeture.</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    // --- Style étendu de la fenêtre flottante ---

    /// <summary>Lit un long de la fenêtre (version 64-bit safe).</summary>
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    public static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    /// <summary>Écrit un long de la fenêtre (version 64-bit safe).</summary>
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    public static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr newValue);

    /// <summary>
    /// Active la fenêtre malgré WS_EX_NOACTIVATE — utilisé sur clic manuel dans la
    /// fenêtre flottante, ou à chaque capture si le toggle « Auto-focus » est actif.
    /// </summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hwnd);

    /// <summary>
    /// Attache notre file d'input à celle d'un autre thread. Nécessaire pour que
    /// SetForegroundWindow soit honoré depuis l'arrière-plan : Windows ne l'accorde
    /// qu'au thread qui « possède » l'input courant.
    /// </summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool attach);

    /// <summary>Id du thread appelant (pour AttachThreadInput).</summary>
    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();
}
