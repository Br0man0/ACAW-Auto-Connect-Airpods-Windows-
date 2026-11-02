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
    private TimeSpan _idleDelay = options.SilenceDelay; // délai de relâche à appliquer à la connexion si rien ne joue
    private bool _retakeAfterRelease;      // une nouvelle lecture a démarré pendant la relâche
    private bool _held;                    // amenés à la main : on les garde jusqu'au prochain changement de lecture
    private bool _muted;                   // haut-parleurs coupés par nous (R7)
    private DateTimeOffset _mutedAt;
    private DateTimeOffset? _unmuteAt;     // AirPods arrivés : rétablissement programmé

    public SwitchState State { get; private set; } = SwitchState.Away;

    public IReadOnlyList<SwitchCommand> Handle(SwitchEvent e)
    {
        var commands = e switch
        {
            PlaybackStarted => OnPlaybackStarted(),
            PlaybackPaused => OnPlaybackStopped(options.PauseDelay),
            SilenceDetected => OnPlaybackStopped(options.SilenceDelay),
            ReleaseRequested => OnReleaseRequested(),
            TakeRequested => OnTakeRequested(),
            EndpointConnected => OnConnected(),
            EndpointDisconnected => OnDisconnected(),
            AirPodsSeen => OnAirPodsSeen(),
            Tick => OnTick(),
            _ => None,
        };
        return ShouldUnmute() ? [.. commands, SwitchCommand.UnmuteSpeakers] : commands;
    }

    private DateTimeOffset Now => time.GetUtcNow();

    private bool AirPodsAwake => _lastSeen is { } seen && Now - seen <= options.AwakeWindow;

    // R1 : prendre les AirPods au démarrage d'une lecture.
    private IReadOnlyList<SwitchCommand> OnPlaybackStarted()
    {
        if (_playing) return None; // pas une nouvelle lecture : rien à décider (R5)
        _playing = true;
        _held = false;
        _releaseAt = null;         // une reprise annule la relâche programmée
        _idleDelay = options.SilenceDelay;

        if (State == SwitchState.Releasing)
        {
            // La déconnexion est déjà partie et ne s'annule pas : on reprendra les AirPods dès qu'elle sera confirmée.
            _retakeAfterRelease = true;
            return None;
        }

        bool canTake = State == SwitchState.Away
                    || (State == SwitchState.Lost && Now - _lostAt >= options.Cooldown);
        if (!canTake || !AirPodsAwake) return None;
        return StartSwitching();
    }

    // R2 et R3 : programmer la relâche. Une pause explicite a un délai plus court qu'un silence,
    // et un silence qui suit une pause ne doit pas repousser la relâche déjà prévue.
    // Si les AirPods ne sont pas encore connectés, on retient le délai pour l'appliquer à la connexion.
    private IReadOnlyList<SwitchCommand> OnPlaybackStopped(TimeSpan delay)
    {
        _playing = false;
        _held = false;
        _retakeAfterRelease = false;
        if (delay < _idleDelay) _idleDelay = delay;
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
        _held = false;
        StartReleasing();
        return commands;
    }

    // « Amener sur ce PC » : demande explicite, donc ni cooldown ni éveil BLE exigé.
    // Les AirPods sont gardés (pas de relâche pour silence) jusqu'au prochain début ou fin de lecture.
    private IReadOnlyList<SwitchCommand> OnTakeRequested()
    {
        _held = true;
        switch (State)
        {
            case SwitchState.Away or SwitchState.Lost:
                return StartSwitching();
            case SwitchState.Releasing:
                _retakeAfterRelease = true; // la déconnexion en cours ne s'annule pas : on les reprendra après
                return None;
            default:
                _releaseAt = null;
                return None;
        }
    }

    // Connexion attendue (Switching) ou non demandée (Windows qui reconnecte de lui-même) :
    // dans les deux cas les AirPods sont là. Si rien ne joue, on les rendra après le délai retenu :
    // 5 s si une pause a eu lieu pendant la connexion, 30 s sinon (sauf s'ils ont été amenés à la main).
    private IReadOnlyList<SwitchCommand> OnConnected()
    {
        State = SwitchState.OnWindows;
        _releaseAt = _playing || _held ? null : Now + _idleDelay;
        if (_muted) _unmuteAt = Now + options.UnmuteDelay; // laisser à Windows le temps de passer le son aux AirPods
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
                _held = false;
                return _playing ? [SwitchCommand.PauseMedia] : None;
            case SwitchState.Releasing:
                State = SwitchState.Away;
                if (_retakeAfterRelease && (AirPodsAwake || _held)) return StartSwitching();
                return None;
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
                // Déconnexion non confirmée : on considère les AirPods toujours là et on réessaiera,
                // sauf si une lecture a repris entre-temps.
                State = SwitchState.OnWindows;
                _retakeAfterRelease = false;
                _releaseAt = _playing || _held ? null : Now + options.SilenceDelay;
                return None;
            case SwitchState.OnWindows when _releaseAt is { } at && Now >= at:
                StartReleasing();
                return [SwitchCommand.Disconnect];
            default:
                return None;
        }
    }

    // R7 : si quelque chose joue, on coupe les haut-parleurs le temps que les AirPods arrivent.
    private IReadOnlyList<SwitchCommand> StartSwitching()
    {
        State = SwitchState.Switching;
        _waitUntil = Now + options.SwitchTimeout;
        if (!options.MuteWhileSwitching || !_playing || _muted) return [SwitchCommand.Reconnect];

        _muted = true;
        _mutedAt = Now;
        _unmuteAt = null;
        return [SwitchCommand.Reconnect, SwitchCommand.MuteSpeakers];
    }

    // R7 : le son revient dans tous les cas, et au plus tard après MaxMuteDuration. Vérifié après chaque
    // événement : on ne peut donc jamais laisser le PC muet par oubli d'un cas.
    private bool ShouldUnmute()
    {
        if (!_muted) return false;
        bool done = State is not (SwitchState.Switching or SwitchState.OnWindows) // abandon, relâche ou perte
                 || Now - _mutedAt >= options.MaxMuteDuration                      // les AirPods tardent : on rend le son
                 || (_unmuteAt is { } at && Now >= at);                            // arrivés, et Windows a basculé le son
        if (done)
        {
            _muted = false;
            _unmuteAt = null;
        }
        return done;
    }

    private void StartReleasing()
    {
        State = SwitchState.Releasing;
        _releaseAt = null;
        _retakeAfterRelease = false;
        _waitUntil = Now + options.SwitchTimeout;
    }

    private IReadOnlyList<SwitchCommand> OnAirPodsSeen()
    {
        _lastSeen = Now;
        return None;
    }
}
