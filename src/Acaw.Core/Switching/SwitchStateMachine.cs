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
    private bool? _link;                   // liaison radio : établie, coupée, ou null si illisible (bascule suivie)
    private DateTimeOffset _linkClock;     // depuis quand on attend la liaison (début de bascule, ou sa coupure)
    private bool _linkCameUp;              // la liaison s'est établie pendant cette bascule
    private bool _linkLost;                // … puis s'est coupée : la bascule a échoué
    private DateTimeOffset? _startedAt;    // dernier démarrage d'ACAW ou sortie de veille (Started)

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
            RadioLinkUp => OnLink(true),
            RadioLinkDown => OnLink(false),
            RadioLinkUnknown => OnLink(null),
            Tick => OnTick(),
            Started => OnStarted(),
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

        // R7 : les AirPods sont déjà en route (« Amener sur ce PC » sans rien qui jouait) : le son ne doit pas
        // sortir des haut-parleurs en attendant.
        if (State == SwitchState.Switching) return MuteIfPlaying() ? [SwitchCommand.MuteSpeakers] : None;

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
        if (State == SwitchState.Switching)
        {
            // Les AirPods ne sont pas encore là : il n'y a aucune déconnexion à attendre. Avant (recette S3 du
            // 2026-11-21), la machine restait 30 s en Releasing, ignorait « Amener sur ce PC », puis se croyait
            // à tort sur le PC. Disconnect annule l'essai ; s'ils arrivent quand même, ce sera une connexion non
            // demandée, relâchée comme les autres.
            State = SwitchState.Away;
            _releaseAt = null;
            _retakeAfterRelease = false;
            return commands;
        }
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
        bool requested = State == SwitchState.Switching;
        State = SwitchState.OnWindows;
        // Au démarrage du PC (ou au réveil), Windows reconnecte de lui-même les AirPods et les arrache à l'iPhone
        // (journal du 2026-11-26) : personne ne les a demandés, on les rend vite.
        var delay = !requested && InStartupWindow ? options.StartupReleaseDelay : _idleDelay;
        _releaseAt = _playing || _held ? null : Now + delay;
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
            case SwitchState.Switching when LinkGivenUp:
                return AbandonSwitch(); // pas de liaison à temps : ils ne viendront pas
            case SwitchState.Switching when Now >= _waitUntil:
                State = SwitchState.Away; // connexion trop longue : on abandonne
                return None;
            case SwitchState.Releasing when Now >= _waitUntil && _link == false:
                // Déconnexion non confirmée par la sortie audio, mais la liaison radio est coupée : les AirPods
                // ne sont certainement plus sur le PC.
                State = SwitchState.Away;
                if (_retakeAfterRelease && (AirPodsAwake || _held)) return StartSwitching();
                _retakeAfterRelease = false;
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
        _linkClock = Now;
        _linkCameUp = false;
        _linkLost = false;
        return MuteIfPlaying() ? [SwitchCommand.Reconnect, SwitchCommand.MuteSpeakers] : [SwitchCommand.Reconnect];
    }

    // Bascule suivie (R7) : on note ce que fait la liaison pendant la bascule.
    private IReadOnlyList<SwitchCommand> OnLink(bool? up)
    {
        bool? previous = _link;
        _link = up;
        if (State != SwitchState.Switching || up == previous) return None;
        if (up == true) _linkCameUp = true;
        else if (up == false)
        {
            // Établie puis coupée pendant la bascule : échec. Coupée alors qu'elle n'était qu'un reste de la
            // relâche précédente (~3 s mesurées) : on attend la nouvelle liaison, LinkTimeout à partir de là.
            if (_linkCameUp)
            {
                _linkLost = true;
                return AbandonSwitch();
            }
            _linkClock = Now;
        }
        return None;
    }

    // Bascule suivie : les AirPods ne viendront pas. On abandonne tout de suite (demande de Romsy, recette S3 du
    // 2026-11-21) : l'icône repasse au gris et « Amener sur ce PC » redevient possible. Disconnect annule l'essai
    // en cours chez Windows ; s'ils arrivent quand même, ce sera une connexion non demandée, relâchée comme les autres.
    private IReadOnlyList<SwitchCommand> AbandonSwitch()
    {
        State = SwitchState.Away;
        _held = false;
        _retakeAfterRelease = false;
        return [SwitchCommand.Disconnect];
    }

    // Bascule suivie : les AirPods ne viendront pas (pas de liaison à temps, ou liaison perdue en route).
    private bool LinkGivenUp => State == SwitchState.Switching && _link == false
                                && (_linkLost || Now - _linkClock >= options.LinkTimeout);

    // true si on vient de couper les haut-parleurs (et il faut alors envoyer MuteSpeakers).
    private bool MuteIfPlaying()
    {
        if (!options.MuteWhileSwitching || !_playing || _muted || LinkGivenUp) return false;
        _muted = true;
        _mutedAt = Now;
        _unmuteAt = null;
        return true;
    }

    // R7 : le son revient dans tous les cas. Vérifié après chaque événement : on ne peut donc jamais laisser le PC
    // muet par oubli d'un cas. Bascule suivie : tant que la liaison radio est établie, les AirPods arrivent, et
    // seule la fin de la bascule (SwitchTimeout, 30 s) borne la coupure ; sans liaison lisible, MuteTimeout (20 s).
    private bool ShouldUnmute()
    {
        if (!_muted) return false;
        bool done = State is not (SwitchState.Switching or SwitchState.OnWindows) // abandon, relâche ou perte
                 || (_unmuteAt is { } at && Now >= at)                            // arrivés, et Windows a basculé le son
                 || LinkGivenUp                                                   // ils ne viendront pas
                 || (_link is null && Now - _mutedAt >= options.MuteTimeout)      // liaison illisible : limite fixe
                 || Now - _mutedAt >= options.SwitchTimeout;                      // garde-fou absolu, quoi qu'il arrive
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

    // ACAW vient de démarrer, ou le PC sort de veille : début de la fenêtre de démarrage.
    private IReadOnlyList<SwitchCommand> OnStarted()
    {
        _startedAt = Now;
        return None;
    }

    /// <summary>Dans les StartupWindow qui suivent le démarrage d'ACAW ou la sortie de veille.</summary>
    public bool InStartupWindow => _startedAt is { } started && Now - started <= options.StartupWindow;

    private IReadOnlyList<SwitchCommand> OnAirPodsSeen()
    {
        _lastSeen = Now;
        return None;
    }
}
