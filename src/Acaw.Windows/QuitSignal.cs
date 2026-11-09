using System.Diagnostics;

namespace Acaw.Windows;

/// <summary>
/// Demander à acaw-tray de s'arrêter proprement (« acaw quit », utilisé par l'installateur avant une mise à
/// jour ou une désinstallation). Arrêt propre = l'hôte s'arrête et le son coupé par ACAW est rétabli, ce que
/// « taskkill » ne permet pas. Le signal est un événement Windows nommé, propre à la session.
/// </summary>
/// <param name="name">Nom de l'événement : celui d'ACAW par défaut, un nom unique dans les tests.</param>
public sealed class QuitSignal(string name = QuitSignal.DefaultName)
{
    public const string DefaultName = @"Local\ACAW.Quit";

    /// <summary>Côté acaw-tray : crée l'événement que l'on attendra.</summary>
    public EventWaitHandle Listen() => new(initialState: false, EventResetMode.AutoReset, name);

    /// <summary>
    /// Côté « acaw quit » : signale l'arrêt, puis attend que acaw-tray.exe ait disparu (au plus
    /// <paramref name="wait"/>). Renvoie false si ACAW tourne encore après ce délai, true sinon (arrêté, ou
    /// déjà absent).
    /// </summary>
    public bool Request(TimeSpan wait)
    {
        if (EventWaitHandle.TryOpenExisting(name, out var signal))
        {
            using (signal) signal.Set();
        }
        // Seulement la session courante : l'événement est « Local\… », l'ACAW d'un autre utilisateur ne l'entend pas.
        int session = Process.GetCurrentProcess().SessionId;
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < wait)
        {
            var running = Process.GetProcessesByName("acaw-tray");
            bool mine = running.Any(p => p.SessionId == session);
            foreach (var p in running) p.Dispose();
            if (!mine) return true;
            Thread.Sleep(100);
        }
        return false;
    }
}
