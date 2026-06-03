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

    // --- Style étendu de la fenêtre flottante ---

    /// <summary>Lit un long de la fenêtre (version 64-bit safe).</summary>
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    public static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    /// <summary>Écrit un long de la fenêtre (version 64-bit safe).</summary>
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    public static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr newValue);

    /// <summary>
    /// Active la fenêtre malgré WS_EX_NOACTIVATE — utilisé uniquement sur clic manuel
    /// de l'utilisateur dans la fenêtre flottante, pour pouvoir y taper du texte.
    /// </summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hwnd);
}
