using System.Threading.Channels;
using Acaw.Core.Audio;
using Acaw.Core.Config;
using Acaw.Core.Switching;

namespace Acaw.Core.Hosting;

/// <summary>
/// Le programme résident (ADR-0002) : tout ce qui arrive (son, médias, casque, BLE, menu, minuteur) est mis
/// dans une file unique, puis traité un élément à la fois. La machine à états et le tracker ne sont donc
/// jamais appelés par deux threads en même temps, et l'ordre des événements est conservé.
/// </summary>
public sealed class AcawHost : IDisposable
{
    /// <summary>Intervalle du battement : relevé du volume et avancée des minuteurs.</summary>
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Filet de sécurité : l'état du casque est relu à cet intervalle, même sans notification de Windows.
    /// Couvre une notification manquée, ou un casque devenu joignable alors qu'on ne le surveillait plus
    /// (Bluetooth coupé au lancement puis rallumé).
    /// </summary>
    public static readonly TimeSpan HeadsetCheckInterval = TimeSpan.FromSeconds(30);

    /// <summary>Attente maximale de la fin du traitement en cours à l'arrêt (un appel à Windows bloqué, par exemple).</summary>
    public static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(1);

    /// <summary>Après une bascule de profil, délai avant de relire le casque : les coupures mesurées durent ~1 s.</summary>
    public static readonly TimeSpan ProfileSettleDelay = TimeSpan.FromSeconds(3);

    /// <summary>Durée maximale d'une bascule de profil (~3 s mesurées) : au-delà, on relit le casque quoi qu'il arrive.</summary>
    public static readonly TimeSpan MaxProfileChange = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan RadarRetryDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SeenThrottle = TimeSpan.FromSeconds(1);

    private readonly Channel<Input> _inputs = Channel.CreateUnbounded<Input>(new() { SingleReader = true });
    private readonly SwitchOptions _options;
    private readonly IHeadsetPort _headset;
    private readonly IMediaPort _media;
    private readonly ISoundMeter _meter;
    private readonly ISpeakers _speakers;
    private readonly IAirPodsRadar _radar;
    private readonly TimeProvider _time;
    private readonly Action<string> _log;
    private readonly SwitchStateMachine _machine;
    private readonly CancellationTokenSource _stop = new();

    private AudioActivityTracker _tracker;
    private ITimer? _heartbeat;
    private Task? _loop;                     // traitement de la file en arrière-plan
    private bool _connected;                 // dernier état du casque transmis à la machine
    private bool _profileChanging;           // bascule du profil mains libres en cours : coupures ignorées
    private DateTimeOffset? _profileSettleAt; // fin de la bascule : relecture du casque
    private DateTimeOffset? _radarRetryAt;   // redémarrage programmé de l'écoute BLE
    private DateTimeOffset _nextHeadsetCheck; // prochaine relecture de sécurité de l'état du casque
    private long _lastSeenPost;              // anti-inondation des annonces BLE (horodatage)
    private volatile int _state = (int)SwitchState.Away;
    private volatile bool _autoSwitch;
    private bool _speakersRestored;          // rétablissement de sécurité fait au premier démarrage

    public AcawHost(AcawConfig config, IHeadsetPort headset, IMediaPort media, ISoundMeter meter,
                    ISpeakers speakers, IAirPodsRadar radar, TimeProvider time, Action<string> log)
    {
        _options = config.Switching;
        _autoSwitch = config.AutoSwitch;
        (_headset, _media, _meter, _speakers, _radar, _time, _log) = (headset, media, meter, speakers, radar, time, log);
        _machine = new SwitchStateMachine(_options, time);
        _tracker = new AudioActivityTracker(_options, time);
    }

    /// <summary>État courant, lisible depuis n'importe quel thread (par l'icône, par exemple).</summary>
    public SwitchState State => (SwitchState)_state;

    public bool AutoSwitch => _autoSwitch;

    /// <summary>Levé après chaque changement d'état, depuis le thread de l'hôte.</summary>
    public event Action<SwitchState>? StateChanged;

    /// <summary>
    /// Branche les ports et lance le battement. Avec <paramref name="background"/> à false (tests), rien ne
    /// traite la file tout seul : c'est à l'appelant d'appeler <see cref="ProcessPending"/>.
    /// </summary>
    public void Start(bool background = true)
    {
        _headset.StateChanged += OnHeadsetChanged;
        _media.PlayingChanged += OnMediaChanged;
        _radar.AirPodsSeen += OnAirPodsSeen;
        _radar.Stopped += OnRadarStopped;
        Enqueue(new Sync());
        _heartbeat = _time.CreateTimer(_ => Enqueue(new Heartbeat()), null, HeartbeatInterval, HeartbeatInterval);
        if (background) _loop = Task.Run(RunAsync);
    }

    /// <summary>Transmet une demande de l'utilisateur (<see cref="TakeRequested"/>, <see cref="ReleaseRequested"/>).</summary>
    public void Request(SwitchEvent e) => Enqueue(new Forward(e));

    /// <summary>Suspend ou reprend la bascule automatique.</summary>
    public void SetAutoSwitch(bool on) => Enqueue(new SetAuto(on));

    /// <summary>À appeler au réveil de la veille : tout est relu et l'écoute BLE relancée (risque R6).</summary>
    public void OnResumed() => Enqueue(new Sync());

    /// <summary>
    /// Le profil mains libres va être activé ou retiré : les coupures du casque qui suivent ne sont pas une
    /// perte des AirPods. À encadrer par <see cref="EndProfileChange"/>.
    /// </summary>
    public void BeginProfileChange() => Enqueue(new ProfileChange(Starting: true));

    /// <summary>Le profil a changé : l'état réel du casque est relu <see cref="ProfileSettleDelay"/> plus tard.</summary>
    public void EndProfileChange() => Enqueue(new ProfileChange(Starting: false));

    /// <summary>Traite tout ce qui attend dans la file. Renvoie le nombre d'éléments traités.</summary>
    public int ProcessPending()
    {
        int count = 0;
        // Après Dispose, on s'arrête même s'il reste des éléments : plus aucun ordre ne doit partir.
        while (!_stop.IsCancellationRequested && _inputs.Reader.TryRead(out var input))
        {
            count++;
            try
            {
                Process(input);
            }
            catch (Exception e)
            {
                // Une erreur imprévue ne doit jamais arrêter l'application résidente : on la note et on continue.
                Log($"Erreur en traitant {input} : {e}");
            }
        }
        return count;
    }

    /// <summary>
    /// Arrête l'hôte. Attend que l'élément en cours de traitement soit fini (au plus <see cref="StopTimeout"/>) :
    /// quand la configuration change, l'ancien hôte ne doit plus rien envoyer une fois le nouveau démarré.
    /// Ne jamais l'appeler depuis le thread de l'hôte (un abonné à <see cref="StateChanged"/>) : il s'attendrait lui-même.
    /// </summary>
    public void Dispose()
    {
        _stop.Cancel();
        _heartbeat?.Dispose();
        try
        {
            _loop?.Wait(StopTimeout);
        }
        catch (AggregateException)
        {
            // la boucle a échoué : elle est arrêtée quand même
        }
        _headset.StateChanged -= OnHeadsetChanged;
        _media.PlayingChanged -= OnMediaChanged;
        _radar.AirPodsSeen -= OnAirPodsSeen;
        _radar.Stopped -= OnRadarStopped;
        _radar.Stop();
    }

    private async Task RunAsync()
    {
        try
        {
            while (await _inputs.Reader.WaitToReadAsync(_stop.Token)) ProcessPending();
        }
        catch (OperationCanceledException)
        {
            // arrêt demandé
        }
    }

    private void Process(Input input)
    {
        switch (input)
        {
            case Heartbeat:
                if (_autoSwitch) Feed(_tracker.OnPeak(_meter.ReadPeak()));
                Feed([new Tick()]);
                if (_radarRetryAt is { } at && _time.GetUtcNow() >= at) StartRadar();
                if (_profileSettleAt is { } settled && _time.GetUtcNow() >= settled) EndProfileSuppression();
                if (_time.GetUtcNow() >= _nextHeadsetCheck) CheckHeadset();
                break;
            case ProfileChange(true):
                // Garde-fou : si la fin n'est jamais annoncée (erreur pendant l'opération), on relit quand même.
                _profileSettleAt = _time.GetUtcNow() + MaxProfileChange;
                if (!_profileChanging) Log("Bascule du profil mains libres : les coupures du casque sont ignorées");
                _profileChanging = true;
                break;
            case ProfileChange(false):
                if (_profileChanging) _profileSettleAt = _time.GetUtcNow() + ProfileSettleDelay;
                break;
            case Forward(var e):
                Feed([e]);
                break;
            case MediaChanged:
                // On relit l'état réel au lieu de croire le message : deux changements très rapprochés
                // peuvent arriver dans le désordre depuis les threads de Windows.
                if (_autoSwitch) Feed(_tracker.OnMediaPlaying(_media.IsPlaying));
                break;
            case HeadsetChanged(var state):
                ApplyHeadsetState(state);
                break;
            case RadarStopped:
                Log("Écoute BLE arrêtée, nouvel essai dans 10 s");
                _radarRetryAt = _time.GetUtcNow() + RadarRetryDelay;
                break;
            case SetAuto(var on):
                _autoSwitch = on;
                Log(on ? "Bascule automatique activée" : "Bascule automatique suspendue");
                // Le tracker n'a rien reçu pendant la pause : on repart de l'état réel des lecteurs.
                _tracker = new AudioActivityTracker(_options, _time);
                if (on) Feed(_tracker.OnMediaPlaying(_media.IsPlaying));
                break;
            case Sync:
                Log("Synchronisation avec l'état réel");
                if (!_speakersRestored)
                {
                    // Si ACAW s'est arrêté brutalement pendant une coupure (R7), le son revient ici.
                    _speakersRestored = true;
                    _speakers.Unmute();
                }
                CheckHeadset();
                if (_autoSwitch) Feed(_tracker.OnMediaPlaying(_media.IsPlaying));
                StartRadar();
                break;
        }
    }

    private void CheckHeadset()
    {
        _nextHeadsetCheck = _time.GetUtcNow() + HeadsetCheckInterval;
        ApplyHeadsetState(_headset.GetState());
    }

    // Le journal est fourni de l'extérieur : s'il échoue, on perd la ligne, jamais la boucle de l'hôte.
    private void Log(string message)
    {
        try
        {
            _log(message);
        }
        catch (Exception)
        {
            // rien de mieux à faire : il n'y a plus où écrire
        }
    }

    // Fin de la bascule de profil : on relit l'état réel ; seul un vrai changement atteint la machine.
    private void EndProfileSuppression()
    {
        _profileSettleAt = null;
        _profileChanging = false;
        Log("Bascule du profil mains libres terminée : relecture du casque");
        CheckHeadset();
    }

    private void ApplyHeadsetState(HeadsetState state)
    {
        // Pendant une bascule du profil mains libres, Windows coupe le lien quelques dixièmes de seconde, deux
        // fois (mesuré le 2026-11-09). Transmises, ces coupures feraient croire que l'iPhone a pris les AirPods :
        // pause de la vidéo (R6), puis relâche 5 s plus tard (R2). On les ignore ; _connected reste la
        // dernière valeur transmise, et la relecture de fin ne transmet que ce qui a vraiment changé.
        if (_profileChanging) return;
        bool connected = state == HeadsetState.Connected;
        if (connected == _connected) return; // Windows peut répéter la même notification
        _connected = connected;
        Feed([connected ? new EndpointConnected() : new EndpointDisconnected()]);
    }

    private void StartRadar()
    {
        _radarRetryAt = null;
        try
        {
            _radar.Stop();
            _radar.Start();
        }
        catch (Exception e)
        {
            // Le type et le code d'erreur aussi : certaines erreurs de Windows arrivent avec un message vide.
            Log($"Écoute BLE impossible ({e.GetType().Name} 0x{e.HResult:X8} : {e.Message}), nouvel essai dans 10 s");
            _radarRetryAt = _time.GetUtcNow() + RadarRetryDelay;
        }
    }

    private void Feed(IEnumerable<SwitchEvent> events)
    {
        foreach (var e in events)
        {
            var before = _machine.State;
            var commands = _machine.Handle(e);
            // La cause d'abord, puis les commandes : le journal se lit dans l'ordre (utile pour les mesures).
            if (e is not (Tick or AirPodsSeen)) Log($"Événement {e.GetType().Name} ({before} → {_machine.State})");
            else if (_machine.State != before || commands.Count > 0) Log($"Délai écoulé ({before} → {_machine.State})");
            foreach (var command in commands) Execute(command);
            if (_machine.State != before)
            {
                _state = (int)_machine.State;
                StateChanged?.Invoke(_machine.State);
            }
        }
    }

    private void Execute(SwitchCommand command)
    {
        Log($"Commande {command}");
        try
        {
            switch (command)
            {
                case SwitchCommand.Reconnect: _headset.Connect(); break;
                case SwitchCommand.Disconnect: _headset.Disconnect(); break;
                case SwitchCommand.PauseMedia: _media.PauseAll(); break;
                case SwitchCommand.MuteSpeakers: _speakers.Mute(); break;
                case SwitchCommand.UnmuteSpeakers: _speakers.Unmute(); break;
            }
        }
        catch (Exception e)
        {
            // Si l'ordre échoue, la machine attend sa confirmation puis abandonne au bout du délai.
            Log($"Échec de {command} : {e.Message}");
        }
    }

    private void Enqueue(Input input) => _inputs.Writer.TryWrite(input);

    private void OnHeadsetChanged(HeadsetState state) => Enqueue(new HeadsetChanged(state));

    private void OnMediaChanged(bool _) => Enqueue(new MediaChanged());

    private void OnRadarStopped() => Enqueue(new RadarStopped());

    // Les AirPods émettent ~1 annonce par seconde, et l'écoute peut en recevoir plusieurs d'un coup :
    // une seule par seconde suffit à la machine pour savoir qu'ils sont éveillés.
    private void OnAirPodsSeen()
    {
        long now = _time.GetTimestamp();
        long last = Interlocked.Read(ref _lastSeenPost);
        if (last != 0 && _time.GetElapsedTime(last, now) < SeenThrottle) return;
        Interlocked.Exchange(ref _lastSeenPost, now);
        Enqueue(new Forward(new AirPodsSeen()));
    }

    // Ce qui peut arriver dans la file.
    private abstract record Input;
    private sealed record Heartbeat : Input;
    private sealed record Forward(SwitchEvent Event) : Input;
    private sealed record MediaChanged : Input;
    private sealed record HeadsetChanged(HeadsetState State) : Input;
    private sealed record RadarStopped : Input;
    private sealed record SetAuto(bool On) : Input;
    private sealed record Sync : Input;
    private sealed record ProfileChange(bool Starting) : Input;
}
