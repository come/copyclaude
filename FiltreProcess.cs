using System.Diagnostics;
using System.IO;

namespace CopyClaude;

/// <summary>
/// Filtre par nom de process, partagé entre l'écoute presse-papier et le suivi
/// du premier plan. Allowlist configurable via <c>allowlist.txt</c> à côté de
/// l'exe (un nom de process par ligne, sans extension, <c>#</c> pour commenter).
/// </summary>
internal static class FiltreProcess
{
    /// <summary>Allowlist par défaut des process terminal (nom sans extension, insensible à la casse).</summary>
    private static readonly string[] AllowlistParDefaut =
        ["WindowsTerminal", "pwsh", "powershell", "conhost", "Code"];

    private static readonly HashSet<string> Allowlist = ChargerAllowlist();

    /// <summary>Vrai si la fenêtre appartient à un process de l'allowlist.</summary>
    public static bool EstProcessTerminal(IntPtr hwnd)
    {
        var pid = PidDeLaFenetre(hwnd);
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
    public static bool EstNotreProcess(IntPtr hwnd) =>
        PidDeLaFenetre(hwnd) == (uint)Environment.ProcessId;

    private static uint PidDeLaFenetre(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return 0;
        Native.GetWindowThreadProcessId(hwnd, out var pid);
        return pid;
    }

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
}
