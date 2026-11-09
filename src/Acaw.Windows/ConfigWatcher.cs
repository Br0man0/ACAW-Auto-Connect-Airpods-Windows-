namespace Acaw.Windows;

/// <summary>
/// Surveille config.json. Un éditeur écrit souvent un fichier en plusieurs fois (vider, écrire, renommer) :
/// les notifications rapprochées sont regroupées, et <see cref="Changed"/> n'est levé qu'une fois le calme
/// revenu depuis <see cref="QuietDelay"/>.
/// </summary>
public sealed class ConfigWatcher : IDisposable
{
    public static readonly TimeSpan QuietDelay = TimeSpan.FromMilliseconds(500);

    private readonly FileSystemWatcher _watcher;
    private readonly ITimer _timer;
    private readonly TimeSpan _quiet;

    /// <param name="path">Fichier à surveiller. Son dossier est créé s'il n'existe pas (premier lancement).</param>
    /// <param name="time">Horloge : la vraie par défaut, une fausse dans les tests.</param>
    /// <param name="quietDelay">Délai de calme : <see cref="QuietDelay"/> par défaut.</param>
    public ConfigWatcher(string path, TimeProvider? time = null, TimeSpan? quietDelay = null)
    {
        string folder = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(folder); // FileSystemWatcher refuse un dossier qui n'existe pas
        _quiet = quietDelay ?? QuietDelay;
        _timer = (time ?? TimeProvider.System).CreateTimer(_ => Changed?.Invoke(), null,
                                                           Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _watcher = new FileSystemWatcher(folder, Path.GetFileName(path))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.CreationTime,
        };
        _watcher.Changed += (_, _) => Notify();
        _watcher.Created += (_, _) => Notify();
        _watcher.Deleted += (_, _) => Notify();
        _watcher.Renamed += (_, _) => Notify(); // enregistrement « écrire un fichier temporaire puis renommer »
        _watcher.Error += (_, _) => Notify();   // trop de notifications d'un coup : on relit par prudence
        _watcher.EnableRaisingEvents = true;
    }

    /// <summary>Levé sur un thread du pool, une fois le fichier calme. L'abonné doit revenir sur son propre thread.</summary>
    public event Action? Changed;

    /// <summary>Signale une écriture : relance le délai de calme. Appelé par la surveillance, et par les tests.</summary>
    public void Notify() => _timer.Change(_quiet, Timeout.InfiniteTimeSpan);

    public void Dispose()
    {
        _watcher.Dispose();
        _timer.Dispose();
    }
}
