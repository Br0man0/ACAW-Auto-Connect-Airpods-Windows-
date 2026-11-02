namespace Acaw.Core.Switching;

/// <summary>
/// Les règles de bascule de l'ADR-0003, sans aucune dépendance à Windows.
/// On lui envoie des événements, elle renvoie les commandes à exécuter. Elle n'est pas thread-safe :
/// l'hôte doit lui envoyer les événements un par un, depuis un seul thread (ADR-0002).
/// </summary>
public sealed class SwitchStateMachine(SwitchOptions options, TimeProvider time)
{
    private static readonly SwitchCommand[] None = [];

    private bool _playing;
    private DateTimeOffset? _lastSeen;     // dernière annonce BLE des AirPods
    private DateTimeOffset? _releaseAt;    // relâche programmée (R2, R3)
    private DateTimeOffset _waitUntil;     // fin de l'attente en Switching / Releasing
    private DateTimeOffset _lostAt;        // moment de la perte (R5)

    public SwitchState State { get; private set; } = SwitchState.Away;

    public IReadOnlyList<SwitchCommand> Handle(SwitchEvent e) => e switch
    {
        PlaybackStarted => OnPlaybackStarted(),
        PlaybackPaused => OnPlaybackStopped(options.PauseDelay),
        SilenceDetected => OnPlaybackStopped(options.SilenceDelay),
        ReleaseRequested => OnReleaseRequested(),
        EndpointConnected => OnConnected(),
        EndpointDisconnected => OnDisconnected(),
        AirPodsSeen => OnAirPodsSeen(),
        Tick => OnTick(),
        _ => None,
    };

    private DateTimeOffset Now => time.GetUtcNow();

    private bool AirPodsAwake => _lastSeen is { } seen && Now - seen <= options.AwakeWindow;

    // R1 : prendre les AirPods au démarrage d'une lecture.
    private IReadOnlyList<SwitchCommand> OnPlaybackStarted()
    {
        if (_playing) return None; // pas une nouvelle lecture : rien à décider (R5)
        _playing = true;
        _releaseAt = null;         // une reprise annule la relâche programmée

        bool canTake = State == SwitchState.Away
                    || (State == SwitchState.Lost && Now - _lostAt >= options.Cooldown);
        if (!canTake || !AirPodsAwake) return None;

        State = SwitchState.Switching;
        _waitUntil = Now + options.SwitchTimeout;
        return [SwitchCommand.Reconnect];
    }

    // R2 et R3 : programmer la relâche. Une pause explicite a un délai plus court qu'un silence,
    // et un silence qui suit une pause ne doit pas repousser la relâche déjà prévue.
    private IReadOnlyList<SwitchCommand> OnPlaybackStopped(TimeSpan delay)
    {
        _playing = false;
        if (State == SwitchState.OnWindows)
        {
            var at = Now + delay;
            if (_releaseAt is null || at < _releaseAt) _releaseAt = at;
        }
        return None;
    }

    // R4 : relâche immédiate, en mettant d'abord le PC en pause pour ne pas basculer sur les haut-parleurs.
    private IReadOnlyList<SwitchCommand> OnReleaseRequested()
    {
        if (State is not (SwitchState.OnWindows or SwitchState.Switching)) return None;
        var commands = _playing
            ? new[] { SwitchCommand.PauseMedia, SwitchCommand.Disconnect }
            : [SwitchCommand.Disconnect];
        StartReleasing();
        return commands;
    }

    // Connexion attendue (Switching) ou non demandée (Windows qui reconnecte de lui-même) :
    // dans les deux cas les AirPods sont là. Si rien ne joue, on les rendra après un silence.
    private IReadOnlyList<SwitchCommand> OnConnected()
    {
        State = SwitchState.OnWindows;
        _releaseAt = _playing ? null : Now + options.SilenceDelay;
        return None;
    }

    private IReadOnlyList<SwitchCommand> OnDisconnected()
    {
        switch (State)
        {
            case SwitchState.OnWindows:
                // R5 + R6 : quelqu'un d'autre les a pris. On ne les reprend pas aussitôt,
                // et on coupe le son du PC pour qu'il ne sorte pas des haut-parleurs.
                State = SwitchState.Lost;
                _lostAt = Now;
                _releaseAt = null;
                return _playing ? [SwitchCommand.PauseMedia] : None;
            case SwitchState.Releasing:
            case SwitchState.Switching:
                State = SwitchState.Away;
                return None;
            default:
                return None;
        }
    }

    private IReadOnlyList<SwitchCommand> OnTick()
    {
        switch (State)
        {
            case SwitchState.Switching when Now >= _waitUntil:
                State = SwitchState.Away; // connexion trop longue : on abandonne
                return None;
            case SwitchState.Releasing when Now >= _waitUntil:
                // Déconnexion non confirmée : on considère les AirPods toujours là et on réessaiera.
                State = SwitchState.OnWindows;
                _releaseAt = Now + options.SilenceDelay;
                return None;
            case SwitchState.OnWindows when _releaseAt is { } at && Now >= at:
                StartReleasing();
                return [SwitchCommand.Disconnect];
            default:
                return None;
        }
    }

    private void StartReleasing()
    {
        State = SwitchState.Releasing;
        _releaseAt = null;
        _waitUntil = Now + options.SwitchTimeout;
    }

    private IReadOnlyList<SwitchCommand> OnAirPodsSeen()
    {
        _lastSeen = Now;
        return None;
    }
}
