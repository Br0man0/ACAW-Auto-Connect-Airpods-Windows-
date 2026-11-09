using Acaw.Core.Config;
using Acaw.Core.Hosting;
using Acaw.Core.Switching;
using Microsoft.Extensions.Time.Testing;

namespace Acaw.Core.Tests;

/// <summary>
/// Tests d'intégration de l'hôte : la vraie file, le vrai tracker, la vraie machine, le vrai minuteur
/// (piloté par la fausse horloge), seuls Windows et les AirPods sont simulés. Chaque test raconte une
/// situation de la vie réelle, de bout en bout.
/// </summary>
public sealed class AcawHostTests : IDisposable
{
    private readonly FakeTimeProvider _time = new();
    private readonly FakeHeadset _headset = new();
    private readonly FakeMedia _media = new();
    private readonly FakeMeter _meter = new();
    private readonly FakeSpeakers _speakers = new();
    private readonly FakeRadar _radar = new();
    private readonly List<string> _log = [];
    private AcawHost _host = null!;

    private void Start(bool autoSwitch = true)
    {
        _host = new AcawHost(new AcawConfig { Mac = "A1:B2:C3:D4:E5:F6", AutoSwitch = autoSwitch },
                             _headset, _media, _meter, _speakers, _radar, _time, _log.Add);
        _host.Start(background: false);
        _host.ProcessPending();
    }

    public void Dispose() => _host?.Dispose();

    // Fait avancer le temps par pas de 250 ms (le minuteur ajoute un battement à chaque pas) et traite la file.
    private void Wait(double seconds)
    {
        for (int i = 0; i < seconds * 4; i++)
        {
            _time.Advance(AcawHost.HeartbeatInterval);
            _host.ProcessPending();
        }
    }

    private void Run() => _host.ProcessPending();

    private void AirPodsAwake()
    {
        _radar.Sees();
        Run();
    }

    [Fact]
    public void Video_YouTube_amene_les_AirPods_puis_la_pause_les_rend()
    {
        Start();
        AirPodsAwake();

        _media.Plays(true);
        Run();
        Assert.Equal(1, _headset.Connects);

        _headset.Becomes(HeadsetState.Connected);
        Run();
        Assert.Equal(SwitchState.OnWindows, _host.State);

        _media.Plays(false);
        Wait(4.75);
        Assert.Equal(0, _headset.Disconnects);
        Wait(0.5);
        Assert.Equal(1, _headset.Disconnects);
    }

    [Fact]
    public void Haut_parleurs_coupes_pendant_l_arrivee_puis_retablis()
    {
        Start();
        AirPodsAwake();

        _media.Plays(true);
        Run();
        Assert.True(_speakers.Muted); // le son ne sort pas des haut-parleurs pendant la connexion

        _headset.Becomes(HeadsetState.Connected);
        Run();
        Wait(1.25);
        Assert.False(_speakers.Muted);
    }

    [Fact]
    public void Haut_parleurs_retablis_au_demarrage_apres_un_arret_brutal()
    {
        Start();

        Assert.Equal(1, _speakers.Unmutes); // sans effet si rien n'avait été coupé (cf. SpeakerMute)
    }

    [Fact]
    public void Notification_Discord_ne_vole_pas_les_AirPods()
    {
        Start();
        AirPodsAwake();

        _meter.Peak = 0.4f;
        Wait(1.25);
        _meter.Peak = 0;
        Wait(10);

        Assert.Equal(0, _headset.Connects);
    }

    [Fact]
    public void Jeu_qui_fait_du_son_amene_les_AirPods_apres_2_s()
    {
        Start();
        AirPodsAwake();

        _meter.Peak = 0.4f;
        Wait(1.5);
        Assert.Equal(0, _headset.Connects);
        Wait(1);
        Assert.Equal(1, _headset.Connects);
    }

    [Fact]
    public void AirPods_dans_le_boitier_ferme_ne_sont_pas_appeles()
    {
        Start(); // aucune annonce BLE

        _media.Plays(true);
        Run();

        Assert.Equal(0, _headset.Connects);
    }

    [Fact]
    public void Bascule_automatique_suspendue_n_amene_rien_mais_le_menu_marche()
    {
        Start(autoSwitch: false);
        AirPodsAwake();

        _media.Plays(true);
        Run();
        Assert.Equal(0, _headset.Connects);

        _host.Request(new TakeRequested());
        Run();
        Assert.Equal(1, _headset.Connects);
    }

    [Fact]
    public void Reprise_de_la_bascule_automatique_tient_compte_de_la_lecture_en_cours()
    {
        Start(autoSwitch: false);
        AirPodsAwake();
        _media.Plays(true);
        Run();

        _host.SetAutoSwitch(true);
        Run();

        Assert.True(_host.AutoSwitch);
        Assert.Equal(1, _headset.Connects);
    }

    [Fact]
    public void AirPods_deja_connectes_au_demarrage_sont_reconnus()
    {
        _headset.State = HeadsetState.Connected;

        Start();

        Assert.Equal(SwitchState.OnWindows, _host.State);
    }

    [Fact]
    public void iPhone_qui_reprend_les_AirPods_pendant_une_video_la_met_en_pause()
    {
        Start();
        AirPodsAwake();
        _media.Plays(true);
        Run();
        _headset.Becomes(HeadsetState.Connected);
        Run();

        _headset.Becomes(HeadsetState.Disconnected); // Centre de contrôle de l'iPhone
        Run();

        Assert.Equal(1, _media.Pauses);
        Assert.Equal(SwitchState.Lost, _host.State);
        Wait(10);
        Assert.Equal(1, _headset.Connects); // pas de reprise
    }

    [Fact]
    public void Notification_Windows_repetee_n_est_comptee_qu_une_fois()
    {
        Start();
        AirPodsAwake();
        _media.Plays(true);
        Run();

        _headset.Becomes(HeadsetState.Connected);
        _headset.Becomes(HeadsetState.Connected);
        _headset.Becomes(HeadsetState.Absent);
        _headset.Becomes(HeadsetState.Disconnected); // Absent puis Disconnected : une seule perte
        Run();

        Assert.Equal(1, _media.Pauses);
    }

    [Fact]
    public void Ordre_Windows_qui_echoue_est_note_sans_arreter_l_application()
    {
        _headset.FailWith = new InvalidOperationException("driver indisponible");
        Start();
        AirPodsAwake();

        _media.Plays(true);
        Run();
        Assert.Contains(_log, line => line.Contains("Échec de Reconnect"));

        Wait(31); // la machine abandonne l'attente…
        Assert.Equal(SwitchState.Away, _host.State);

        _headset.FailWith = null; // … et l'application fonctionne toujours
        _media.Plays(false);
        Run();
        AirPodsAwake();
        _media.Plays(true);
        Run();
        Assert.Equal(2, _headset.Connects);
    }

    [Fact]
    public void Ecoute_BLE_arretee_est_relancee_10_s_plus_tard()
    {
        Start();
        Assert.Equal(1, _radar.Starts);

        _radar.Dies(); // Bluetooth coupé
        Wait(9.5);
        Assert.Equal(1, _radar.Starts);
        Wait(1);
        Assert.Equal(2, _radar.Starts);
    }

    [Fact]
    public void Ecoute_BLE_impossible_au_demarrage_est_reessayee()
    {
        _radar.FailOnStart = true;
        Start();

        _radar.FailOnStart = false;
        Wait(10.5);

        Assert.True(_radar.Running);
    }

    [Fact]
    public void Reveil_de_veille_resynchronise_tout()
    {
        Start();
        _headset.State = HeadsetState.Connected; // pendant la veille, sans notification

        _host.OnResumed();
        Run();

        Assert.Equal(SwitchState.OnWindows, _host.State);
        Assert.Equal(2, _radar.Starts);
    }

    [Fact]
    public void Etat_change_est_signale_a_l_icone()
    {
        var seen = new List<SwitchState>();
        Start();
        _host.StateChanged += seen.Add;
        AirPodsAwake();

        _media.Plays(true);
        Run();
        _headset.Becomes(HeadsetState.Connected);
        Run();

        Assert.Equal([SwitchState.Switching, SwitchState.OnWindows], seen);
    }

    [Fact]
    public void Rafale_d_annonces_BLE_n_inonde_pas_la_file()
    {
        Start();

        for (int i = 0; i < 50; i++) _radar.Sees(); // 50 annonces au même instant

        Assert.Equal(1, _host.ProcessPending());
    }

    [Fact]
    public void Journal_en_panne_n_arrete_pas_l_application()
    {
        _host = new AcawHost(new AcawConfig { Mac = "A1:B2:C3:D4:E5:F6" }, _headset, _media, _meter, _speakers, _radar, _time,
                             _ => throw new UnauthorizedAccessException("journal inaccessible"));
        _host.Start(background: false);
        _host.ProcessPending();
        AirPodsAwake();

        _media.Plays(true);
        Run();

        Assert.Equal(1, _headset.Connects); // malgré un journal qui échoue à chaque ligne
    }

    [Fact]
    public void Casque_qui_change_sans_notification_est_vu_dans_les_30_s()
    {
        Start(); // Bluetooth coupé au lancement : rien n'est surveillé
        _headset.State = HeadsetState.Connected; // Windows les reconnecte… sans prévenir

        Wait(29);
        Assert.Equal(SwitchState.Away, _host.State);
        Wait(1.5);
        Assert.Equal(SwitchState.OnWindows, _host.State);
    }

    [Fact]
    public void Message_de_lecture_arrive_en_retard_est_ignore()
    {
        Start();
        AirPodsAwake();

        _media.SendsStale(announced: true); // « ça joue »… alors que plus rien ne joue
        Run();

        Assert.Equal(0, _headset.Connects);
    }

    [Fact]
    public async Task En_arriere_plan_la_file_est_traitee_toute_seule()
    {
        var host = new AcawHost(new AcawConfig { Mac = "A1:B2:C3:D4:E5:F6" },
                                _headset, _media, _meter, _speakers, _radar, TimeProvider.System, _log.Add);
        var connected = new TaskCompletionSource();
        host.StateChanged += state => { if (state == SwitchState.OnWindows) connected.TrySetResult(); };

        host.Start();
        _headset.Becomes(HeadsetState.Connected);

        await connected.Task.WaitAsync(TimeSpan.FromSeconds(5));
        host.Dispose();
    }

    [Fact]
    public async Task Arret_attend_la_fin_de_l_ordre_en_cours_puis_plus_rien_ne_part()
    {
        // Changement de configuration : l'ancien hôte est arrêté pendant qu'il envoie un ordre à Windows.
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        _headset.DuringConnect = () => { entered.Set(); release.Wait(TimeSpan.FromSeconds(5)); };
        _host = new AcawHost(new AcawConfig { Mac = "A1:B2:C3:D4:E5:F6" },
                             _headset, _media, _meter, _speakers, _radar, _time, _log.Add);
        _host.Start();
        _host.Request(new TakeRequested());
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)), "l'ordre Connect n'est jamais parti");

        var stopping = Task.Run(_host.Dispose);

        // Dispose ne doit pas finir tant que Connect est en cours…
        await Assert.ThrowsAsync<TimeoutException>(() => stopping.WaitAsync(TimeSpan.FromMilliseconds(300)));
        release.Set();
        await stopping.WaitAsync(TimeSpan.FromSeconds(5)); // … puis finir dès que Connect rend la main
        Assert.False(_radar.Running);
        _host.Request(new TakeRequested()); // après l'arrêt : ignoré
        Assert.Equal(0, _host.ProcessPending());
        Assert.Equal(1, _headset.Connects);
    }
}
