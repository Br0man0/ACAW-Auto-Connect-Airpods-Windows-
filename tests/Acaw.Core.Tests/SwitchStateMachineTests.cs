using Acaw.Core.Switching;
using Microsoft.Extensions.Time.Testing;
using static Acaw.Core.Switching.SwitchCommand;

namespace Acaw.Core.Tests;

/// <summary>
/// Chaque test raconte un scénario de l'ADR-0003 : on envoie des événements, on avance une fausse
/// horloge, et on vérifie les commandes renvoyées et l'état final.
/// </summary>
public class SwitchStateMachineTests
{
    private readonly FakeTimeProvider _time = new();
    private readonly SwitchStateMachine _machine;

    // La sourdine (R7) est désactivée ici pour vérifier les autres règles seules : elle a ses propres tests
    // dans SpeakerMuteRuleTests, et les invariants tournent avec les réglages par défaut.
    public SwitchStateMachineTests() =>
        _machine = new SwitchStateMachine(new SwitchOptions { MuteWhileSwitching = false }, _time);

    private IReadOnlyList<SwitchCommand> Send(SwitchEvent e) => _machine.Handle(e);

    private IReadOnlyList<SwitchCommand> After(double seconds)
    {
        _time.Advance(TimeSpan.FromSeconds(seconds));
        return Send(new Tick());
    }

    // Amène la machine dans l'état « AirPods sur le PC, une vidéo joue ».
    private void PlayingOnWindows()
    {
        Send(new AirPodsSeen());
        Assert.Equal([Reconnect], Send(new PlaybackStarted()));
        Send(new EndpointConnected());
        Assert.Equal(SwitchState.OnWindows, _machine.State);
    }

    [Fact]
    public void R1_Lecture_avec_AirPods_eveilles_les_prend()
    {
        Send(new AirPodsSeen());

        Assert.Equal([Reconnect], Send(new PlaybackStarted()));
        Assert.Equal(SwitchState.Switching, _machine.State);
    }

    [Fact]
    public void R1_Lecture_avec_AirPods_endormis_ne_fait_rien()
    {
        Send(new AirPodsSeen());
        _time.Advance(TimeSpan.FromSeconds(31)); // plus d'annonce BLE depuis 31 s : boîtier fermé

        Assert.Empty(Send(new PlaybackStarted()));
        Assert.Equal(SwitchState.Away, _machine.State);
    }

    [Fact]
    public void R1_Lecture_qui_continue_n_est_pas_une_nouvelle_lecture()
    {
        PlayingOnWindows();

        Assert.Empty(Send(new PlaybackStarted()));
    }

    [Fact]
    public void Scenario_de_l_ADR_video_mise_en_pause_puis_AirPods_rendus_apres_5_s()
    {
        PlayingOnWindows();
        Send(new PlaybackPaused());

        Assert.Empty(After(4.9));
        Assert.Equal([Disconnect], After(0.2));
        Assert.Equal(SwitchState.Releasing, _machine.State);

        Send(new EndpointDisconnected());
        Assert.Equal(SwitchState.Away, _machine.State);
    }

    [Fact]
    public void R2_Pause_puis_reprise_avant_5_s_ne_rend_pas_les_AirPods()
    {
        PlayingOnWindows();
        Send(new PlaybackPaused());
        _time.Advance(TimeSpan.FromSeconds(3));
        Send(new PlaybackStarted());

        Assert.Empty(After(10));
        Assert.Equal(SwitchState.OnWindows, _machine.State);
    }

    [Fact]
    public void R2_Pause_pendant_la_connexion_rend_les_AirPods_5_s_apres_leur_arrivee()
    {
        Send(new AirPodsSeen());
        Send(new PlaybackStarted());
        Send(new PlaybackPaused());     // pause avant que les AirPods soient arrivés
        Send(new EndpointConnected());

        Assert.Empty(After(4.9));
        Assert.Equal([Disconnect], After(0.2));
    }

    [Fact]
    public void Lecture_pendant_la_relache_reprend_les_AirPods_une_fois_relaches()
    {
        PlayingOnWindows();
        Send(new PlaybackPaused());
        Assert.Equal([Disconnect], After(5.1));

        // L'utilisateur relance la vidéo pendant que la déconnexion est en cours.
        Assert.Empty(Send(new PlaybackStarted()));

        Assert.Equal([Reconnect], Send(new EndpointDisconnected()));
        Assert.Equal(SwitchState.Switching, _machine.State);
    }

    [Fact]
    public void R4_Relache_demandee_pendant_la_lecture_ne_reprend_pas_ensuite()
    {
        PlayingOnWindows();
        Send(new ReleaseRequested());

        Assert.Empty(Send(new EndpointDisconnected()));
        Assert.Equal(SwitchState.Away, _machine.State);
    }

    [Fact]
    public void R3_Silence_sans_pause_rend_les_AirPods_apres_30_s()
    {
        PlayingOnWindows();
        Send(new SilenceDetected());

        Assert.Empty(After(29));
        Assert.Equal([Disconnect], After(1.1));
    }

    [Fact]
    public void R3_Silence_apres_une_pause_ne_repousse_pas_la_relache()
    {
        PlayingOnWindows();
        Send(new PlaybackPaused());
        Send(new SilenceDetected());

        Assert.Equal([Disconnect], After(5.1));
    }

    [Fact]
    public void R4_Relache_demandee_pendant_la_lecture_met_en_pause_puis_rend()
    {
        PlayingOnWindows();

        Assert.Equal([PauseMedia, Disconnect], Send(new ReleaseRequested()));
        Assert.Equal(SwitchState.Releasing, _machine.State);
    }

    [Fact]
    public void R5_R6_Perte_pendant_la_lecture_met_en_pause_et_ne_reprend_pas()
    {
        PlayingOnWindows();

        // L'iPhone prend les AirPods via le Centre de contrôle.
        Assert.Equal([PauseMedia], Send(new EndpointDisconnected()));
        Assert.Equal(SwitchState.Lost, _machine.State);

        // La vidéo continue (appli sans contrôles média) : aucun nouvel événement de lecture,
        // donc aucune reprise, même longtemps après.
        Assert.Empty(After(60));
        Assert.Equal(SwitchState.Lost, _machine.State);
    }

    [Fact]
    public void R5_Nouvelle_lecture_apres_le_cooldown_reprend_les_AirPods()
    {
        PlayingOnWindows();
        Send(new EndpointDisconnected());
        Send(new PlaybackPaused()); // effet de la commande PauseMedia
        _time.Advance(TimeSpan.FromSeconds(6));
        Send(new AirPodsSeen());

        Assert.Equal([Reconnect], Send(new PlaybackStarted()));
    }

    [Fact]
    public void R5_Lecture_automatique_pendant_le_cooldown_ne_reprend_pas()
    {
        PlayingOnWindows();
        Send(new EndpointDisconnected());
        Send(new PlaybackPaused());
        _time.Advance(TimeSpan.FromSeconds(2));

        Assert.Empty(Send(new PlaybackStarted()));
        Assert.Equal(SwitchState.Lost, _machine.State);
    }

    [Fact]
    public void Amener_a_la_main_marche_meme_AirPods_non_vus_en_BLE()
    {
        Assert.Equal([Reconnect], Send(new TakeRequested()));
        Assert.Equal(SwitchState.Switching, _machine.State);
    }

    [Fact]
    public void Amener_a_la_main_passe_outre_le_cooldown()
    {
        PlayingOnWindows();
        Send(new EndpointDisconnected()); // l'iPhone les a pris
        _time.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal([Reconnect], Send(new TakeRequested()));
    }

    [Fact]
    public void Amenes_a_la_main_sans_lecture_ne_sont_pas_rendus_pour_silence()
    {
        Send(new TakeRequested());
        Send(new EndpointConnected());

        Assert.Empty(After(120)); // un appel, une écoute en attente… : ils restent
        Assert.Equal(SwitchState.OnWindows, _machine.State);
    }

    [Fact]
    public void Amenes_a_la_main_puis_lecture_puis_pause_sont_rendus_normalement()
    {
        Send(new TakeRequested());
        Send(new EndpointConnected());
        Send(new PlaybackStarted());
        Send(new PlaybackPaused());

        Assert.Equal([Disconnect], After(5.1));
    }

    [Fact]
    public void Amener_pendant_la_relache_les_reprend_une_fois_relaches()
    {
        PlayingOnWindows();
        Send(new ReleaseRequested());
        Send(new PlaybackPaused());
        Send(new TakeRequested());

        Assert.Equal([Reconnect], Send(new EndpointDisconnected()));
    }

    [Fact]
    public void Connexion_trop_longue_abandonne()
    {
        Send(new AirPodsSeen());
        Send(new PlaybackStarted());

        Assert.Empty(After(31));
        Assert.Equal(SwitchState.Away, _machine.State);
    }

    [Fact]
    public void Connexion_non_demandee_est_acceptee_puis_rendue_apres_un_silence()
    {
        // Windows reconnecte les AirPods de lui-même alors que rien ne joue (risque R3 de l'ADR-0002).
        Send(new EndpointConnected());
        Assert.Equal(SwitchState.OnWindows, _machine.State);

        Assert.Equal([Disconnect], After(30.1));
    }

    [Fact]
    public void Deconnexion_non_confirmee_revient_sur_Windows_pour_reessayer()
    {
        PlayingOnWindows();
        Send(new ReleaseRequested());
        Send(new PlaybackPaused()); // effet de la commande PauseMedia

        Assert.Empty(After(31));
        Assert.Equal(SwitchState.OnWindows, _machine.State);
        Assert.Equal([Disconnect], After(30.1));
    }

    [Fact]
    public void Deconnexion_non_confirmee_avec_lecture_reprise_garde_les_AirPods()
    {
        PlayingOnWindows();
        Send(new PlaybackPaused());
        After(5.1);                  // relâche envoyée
        Send(new PlaybackStarted()); // l'utilisateur relance pendant la relâche

        Assert.Empty(After(31));     // la déconnexion n'est jamais confirmée
        Assert.Equal(SwitchState.OnWindows, _machine.State);
        Assert.Empty(After(60));     // ça joue : pas de nouvelle relâche
    }
}
