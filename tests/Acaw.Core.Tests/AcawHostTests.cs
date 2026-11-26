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
    private readonly FakeRadioLink _link = new();
    private readonly List<string> _log = [];
    private AcawHost _host = null!;

    private void Start(bool autoSwitch = true, bool withLink = false)
    {
        _host = new AcawHost(new AcawConfig { Mac = "A1:B2:C3:D4:E5:F6", AutoSwitch = autoSwitch },
                             _headset, _media, _meter, _speakers, _radar, _time, _log.Add, withLink ? _link : null);
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

    // Laisse passer la fenêtre de démarrage (2 min), pendant laquelle les sons d'applis sont ignorés.
    private void AfterStartupWindow() => Wait(new SwitchOptions().StartupWindow.TotalSeconds + 0.25);

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

    // ── Bascule suivie : la liaison radio relayée par l'hôte ──

    [Fact]
    public void Suivie_bascule_lente_de_25_s_aucune_fuite_grace_a_la_liaison()
    {
        Start(withLink: true);
        AirPodsAwake();
        _media.Plays(true);
        Run();
        Wait(0.5);
        _link.Becomes(LinkState.Up);
        Run();

        Wait(24);
        Assert.True(_speakers.Muted); // au-delà des 20 s : la liaison dit qu'ils arrivent

        _headset.Becomes(HeadsetState.Connected);
        Run();
        Wait(1.25);
        Assert.False(_speakers.Muted);
        Assert.Contains("Événement RadioLinkUp (Switching → Switching)", _log); // trace pour les mesures
    }

    [Fact]
    public void Suivie_AirPods_injoignables_son_rendu_a_6_s()
    {
        Start(withLink: true);
        AirPodsAwake();
        _media.Plays(true);
        Run();

        Wait(5.75);
        Assert.True(_speakers.Muted);
        Wait(0.5);
        Assert.False(_speakers.Muted);
    }

    [Fact]
    public void Suivie_liaison_lue_au_demarrage_et_notifications_repetees_ignorees()
    {
        _link.State = LinkState.Up;
        Start(withLink: true);
        Assert.Contains("Événement RadioLinkUp (Away → Away)", _log);

        _link.Becomes(LinkState.Up); // Windows répète
        Run();
        Assert.Single(_log, line => line.Contains("RadioLinkUp", StringComparison.Ordinal));
    }

    [Fact]
    public void Suivie_liaison_illisible_l_hote_continue_avec_la_limite_fixe()
    {
        _link.FailOnRead = true;
        Start(withLink: true);
        AirPodsAwake();
        _media.Plays(true);
        Run();

        Assert.Contains(_log, line => line.StartsWith("Liaison radio illisible", StringComparison.Ordinal));
        Wait(19.75);
        Assert.True(_speakers.Muted); // sans liaison lisible : comportement d'avant, 20 s
        Wait(0.5);
        Assert.False(_speakers.Muted);
    }

    [Fact]
    public void Suivie_liaison_illisible_notee_une_seule_fois_malgre_les_relectures()
    {
        _link.FailOnRead = true;
        Start(withLink: true);
        Wait(95); // trois relectures de sécurité (toutes les 30 s)
        Assert.Single(_log, line => line.StartsWith("Liaison radio illisible", StringComparison.Ordinal));

        _link.FailOnRead = false; // retour à la normale…
        Wait(30);
        _link.FailOnRead = true;  // … puis nouvelle panne : de nouveau notée
        Wait(30);
        Assert.Equal(2, _log.Count(line => line.StartsWith("Liaison radio illisible", StringComparison.Ordinal)));
    }

    [Fact]
    public void Suivie_l_hote_arrete_n_ecoute_plus_la_liaison()
    {
        Start(withLink: true);
        _host.Dispose();
        _link.Becomes(LinkState.Up);

        Assert.Equal(0, _host.ProcessPending());
    }

    [Fact]
    public void Haut_parleurs_retablis_au_demarrage_apres_un_arret_brutal()
    {
        Start();

        Assert.Equal(1, _speakers.Unmutes); // sans effet si rien n'avait été coupé (cf. SpeakerMute)
    }

    // ── Démarrage du PC (journal du 2026-11-26) ──

    [Fact]
    public void Demarrage_AirPods_reconnectes_par_Windows_rendus_en_2_s()
    {
        _headset.State = HeadsetState.Connected; // Windows les a reconnectés avant le lancement d'ACAW
        Start();
        Assert.Equal(SwitchState.OnWindows, _host.State);
        Assert.Contains("Événement Started (Away → Away)", _log);

        Wait(1.75);
        Assert.Equal(0, _headset.Disconnects);
        Wait(0.5);
        Assert.Equal(1, _headset.Disconnects); // avant : 30 s loin de l'iPhone
    }

    [Fact]
    public void Demarrage_son_d_Opera_GX_au_lancement_ne_prend_pas_les_AirPods()
    {
        Start();
        Wait(50);
        AirPodsAwake();       // éveillés au moment du son : sans la règle, ils seraient pris

        _meter.Peak = 0.4f;   // le son d'Opera GX, ~3,5 s, 51 s après le lancement
        Wait(3.5);
        _meter.Peak = 0;
        Wait(5);

        Assert.Equal(0, _headset.Connects);
    }

    [Fact]
    public void Demarrage_une_vraie_lecture_prend_toujours_les_AirPods()
    {
        Start();
        AirPodsAwake();

        _media.Plays(true); // YouTube, Spotify… : passe par les contrôles média
        Run();

        Assert.Equal(1, _headset.Connects);
    }

    [Fact]
    public void Hors_demarrage_une_connexion_non_demandee_laisse_30_s_comme_avant()
    {
        Start();
        AfterStartupWindow();

        _headset.Becomes(HeadsetState.Connected); // connectés à la main depuis les réglages Bluetooth
        Run();
        Wait(29.5);
        Assert.Equal(0, _headset.Disconnects);
        Wait(0.75);
        Assert.Equal(1, _headset.Disconnects);
    }

    [Fact]
    public void Sortie_de_veille_rouvre_la_fenetre_de_demarrage()
    {
        Start();
        AfterStartupWindow();

        _host.OnResumed();
        _headset.Becomes(HeadsetState.Connected); // Windows reconnecte au réveil
        Run();
        Wait(2.25);

        Assert.Equal(1, _headset.Disconnects);
    }

    [Fact]
    public void Notification_Discord_ne_vole_pas_les_AirPods()
    {
        Start();
        AfterStartupWindow(); // sinon la fenêtre de démarrage ignorerait le son de toute façon
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
        AfterStartupWindow(); // hors de la fenêtre de démarrage, où les sons d'applis sont ignorés
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

    // Vidéo en cours, AirPods sur le PC : la situation où l'on active le micro pour un appel.
    private void WatchingVideoWithAirPods()
    {
        Start();
        AirPodsAwake();
        _media.Plays(true);
        Run();
        _headset.Becomes(HeadsetState.Connected);
        Run();
    }

    [Fact]
    public void Bascule_du_profil_mains_libres_ne_met_pas_en_pause_et_ne_rend_pas_les_AirPods()
    {
        // Mesuré le 2026-11-09 : activer le profil coupe le lien deux fois, quelques dixièmes de seconde.
        WatchingVideoWithAirPods();

        _host.BeginProfileChange();
        Run();
        _headset.Becomes(HeadsetState.Disconnected);
        Run();
        _headset.Becomes(HeadsetState.Connected);
        Run();
        _headset.Becomes(HeadsetState.Disconnected);
        Run();
        _headset.Becomes(HeadsetState.Connected);
        Run();
        _host.EndProfileChange();
        Wait(10);

        Assert.Equal(0, _media.Pauses);
        Assert.Equal(0, _headset.Disconnects);
        Assert.Equal(SwitchState.OnWindows, _host.State);
    }

    [Fact]
    public void Vraie_perte_pendant_la_bascule_est_vue_a_la_relecture()
    {
        // Le profil change, et entre-temps l'iPhone a vraiment pris les AirPods.
        WatchingVideoWithAirPods();

        _host.BeginProfileChange();
        Run();
        _headset.Becomes(HeadsetState.Disconnected);
        Run();
        _host.EndProfileChange();
        Run();
        Assert.Equal(SwitchState.OnWindows, _host.State); // pas encore : on attend que tout se stabilise

        Wait(AcawHost.ProfileSettleDelay.TotalSeconds + 0.5);

        Assert.Equal(SwitchState.Lost, _host.State);
        Assert.Equal(1, _media.Pauses);
    }

    [Fact]
    public void Bascule_jamais_terminee_est_abandonnee_au_bout_du_delai_maximal()
    {
        // L'opération a échoué sans annoncer sa fin : les coupures ne doivent pas être ignorées pour toujours.
        WatchingVideoWithAirPods();
        _host.BeginProfileChange();
        Run();
        _headset.Becomes(HeadsetState.Disconnected);
        Run();

        Wait(AcawHost.MaxProfileChange.TotalSeconds + 0.5);

        Assert.Equal(SwitchState.Lost, _host.State);
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
