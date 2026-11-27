namespace Acaw.Bluetooth.Takeover;

/// <summary>
/// Le garde-fou de la prise en main : si elle échoue trop souvent, l'adaptateur est rendu à son pilote
/// d'origine, pour que le Bluetooth du PC ne reste jamais en panne.
/// </summary>
/// <remarks>
/// Règles :
/// <list type="bullet">
/// <item><see cref="MaxFailures"/> échecs dans une fenêtre de <see cref="FailureWindow"/> → rendre ;</item>
/// <item>une session qui a duré au moins <see cref="HealthySession"/> n'est pas un échec, et efface les échecs passés ;</item>
/// <item>une session arrêtée pour rebrancher l'adaptateur après un réveil n'est pas un échec, même courte.</item>
/// </list>
/// Un seul thread l'appelle (le superviseur du service).
/// </remarks>
public sealed class TakeoverGuard(TimeProvider time)
{
    public const int MaxFailures = 3;
    public static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan HealthySession = TimeSpan.FromSeconds(60);

    private readonly Queue<DateTimeOffset> _failures = new();

    /// <summary>Nombre d'échecs encore comptés (dans la fenêtre).</summary>
    public int RecentFailures
    {
        get
        {
            Prune(time.GetUtcNow());
            return _failures.Count;
        }
    }

    /// <summary>
    /// Une prise en main a échoué avant même de relayer (pilote refusé, attache USB/IP impossible…).
    /// Rend true s'il faut rendre l'adaptateur.
    /// </summary>
    public bool RecordFailure()
    {
        DateTimeOffset now = time.GetUtcNow();
        Prune(now);
        _failures.Enqueue(now);
        return _failures.Count >= MaxFailures;
    }

    /// <summary>
    /// Une session de relais s'est terminée. Rend true s'il faut rendre l'adaptateur.
    /// </summary>
    /// <param name="startedAt">Début de la session.</param>
    /// <param name="replugAfterResume">true si la session a été arrêtée exprès pour rebrancher après un réveil.</param>
    public bool RecordSessionEnd(DateTimeOffset startedAt, bool replugAfterResume)
    {
        if (time.GetUtcNow() - startedAt >= HealthySession)
        {
            _failures.Clear();
            return false;
        }
        return !replugAfterResume && RecordFailure();
    }

    /// <summary>Oublie tous les échecs (l'utilisateur redemande une prise en main).</summary>
    public void Reset() => _failures.Clear();

    private void Prune(DateTimeOffset now)
    {
        while (_failures.Count > 0 && now - _failures.Peek() >= FailureWindow) _failures.Dequeue();
    }
}
