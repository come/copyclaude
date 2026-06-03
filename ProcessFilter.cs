using System.Diagnostics;
using System.IO;

namespace CopyClaude;

/// <summary>
/// Filtre par nom de process, partagé entre l'écoute presse-papier et le suivi
/// du premier plan. Allowlist configurable via <c>allowlist.txt</c> à côté de
/// l'exe (un nom de process par ligne, sans extension, <c>#</c> pour commenter).
/// </summary>
internal static class ProcessFilter
{
    /// <summary>Allowlist par défaut des process terminal (nom sans extension, insensible à la casse).</summary>
    private static readonly string[] DefaultAllowlist =
        ["WindowsTerminal", "pwsh", "powershell", "conhost", "Code"];

    private static readonly HashSet<string> Allowlist = LoadAllowlist();

    /// <summary>Vrai si la fenêtre appartient à un process de l'allowlist.</summary>
    public static bool IsTerminalProcess(IntPtr hwnd)
    {
        var pid = GetWindowPid(hwnd);
        if (pid == 0)
            return false;

        try
        {
            using var process = Process.GetProcessById((int)pid);
            return Allowlist.Contains(process.ProcessName);
        }
        catch (ArgumentException)
        {
            // Le process a disparu entre-temps.
            return false;
        }
    }

    /// <summary>Vrai si la fenêtre appartient à notre propre process (la fenêtre flottante).</summary>
    public static bool IsOwnProcess(IntPtr hwnd) =>
        GetWindowPid(hwnd) == (uint)Environment.ProcessId;

    private static uint GetWindowPid(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return 0;
        Native.GetWindowThreadProcessId(hwnd, out var pid);
        return pid;
    }

    private static HashSet<string> LoadAllowlist()
    {
        var names = DefaultAllowlist.AsEnumerable();
        var path = Path.Combine(AppContext.BaseDirectory, "allowlist.txt");
        if (File.Exists(path))
        {
            var lines = File.ReadAllLines(path)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.StartsWith('#'))
                .ToArray();
            if (lines.Length > 0)
                names = lines;
        }
        return new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
    }
}
