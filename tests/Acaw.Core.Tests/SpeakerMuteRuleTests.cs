using Acaw.Core.Switching;
using Microsoft.Extensions.Time.Testing;
using static Acaw.Core.Switching.SwitchCommand;

namespace Acaw.Core.Tests;

/// <summary>
/// Règle R7 : pendant que les AirPods arrivent, le son ne doit pas sortir des haut-parleurs. On les coupe,
/// et on les rétablit dans TOUS les cas : arrivée des AirPods, échec, abandon, ou au plus tard après 20 s.
/// </summary>
public class SpeakerMuteRuleTests
{
    private readonly FakeTimeProvider _time = new();
    private SwitchStateMachine _machine;

    public SpeakerMuteRuleTests() => _machine = new SwitchStateMachine(new SwitchOptions(), _time);

    private IReadOnlyList<SwitchCommand> Send(SwitchEvent e) => _machine.Handle(e);

    private IReadOnlyList<SwitchCommand> After(double seconds)
    {
        _time.Advance(TimeSpan.FromSeconds(seconds));
        return Send(new Tick());
    }

    private IReadOnlyList<SwitchCommand> VideoStarts()
    {
        Send(new AirPodsSeen());
        return Send(new PlaybackStarted());
    }

    [Fact]
    public void Video_qui_demarre_coupe_les_haut_parleurs_en_appelant_les_AirPods()
    {
        Assert.Equal([Reconnect, MuteSpeakers], VideoStarts());
    }

    [Fact]
    public void AirPods_arrives_le_son_revient_1_s_plus_tard_dans_les_AirPods()
    {
        VideoStarts();
        _time.Advance(TimeSpan.FromSeconds(2));
        Assert.Empty(Send(new EndpointConnected()));

        Assert.Empty(After(0.9));               // Windows bascule le son vers les AirPods…
        Assert.Equal([UnmuteSpeakers], After(0.2)); // … puis on rétablit les haut-parleurs (désormais muets d'eux-mêmes)
    }

    [Fact]
    public void Bascule_lente_de_11_6_s_le_son_ne_sort_jamais_des_haut_parleurs()
    {
        // Relevé du journal de Romsy, 2026-11-09 22:15 : les AirPods sont arrivés en 11,6 s. Avec l'ancienne
        // limite de 6 s, le son était rétabli à 6 s et sortait des haut-parleurs pendant 5,6 s.
        VideoStarts();
        for (int i = 0; i < 46; i++) Assert.Empty(After(0.25)); // 11,5 s de battements : toujours coupé
        _time.Advance(TimeSpan.FromSeconds(0.1));
        Assert.Empty(Send(new EndpointConnected()));

        Assert.Equal([UnmuteSpeakers], After(1.1)); // rétabli seulement une fois les AirPods là
    }

    [Fact]
    public void AirPods_qui_ne_viennent_pas_le_son_revient_sur_les_haut_parleurs_apres_20_s()
    {
        VideoStarts();

        Assert.Empty(After(19.9));
        Assert.Equal([UnmuteSpeakers], After(0.2));
        Assert.Equal(SwitchState.Switching, _machine.State); // la connexion peut encore réussir
    }

    [Fact]
    public void Video_lancee_pendant_un_Amener_sur_ce_PC_coupe_les_haut_parleurs()
    {
        Assert.Equal([Reconnect], Send(new TakeRequested())); // rien ne joue : rien à couper
        _time.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal([MuteSpeakers], Send(new PlaybackStarted())); // les AirPods sont en route : pas de fuite
        _time.Advance(TimeSpan.FromSeconds(2));
        Send(new EndpointConnected());
        Assert.Equal([UnmuteSpeakers], After(1.1));
    }

    [Fact]
    public void Video_lancee_pendant_un_Amener_sur_ce_PC_ne_coupe_pas_si_option_desactivee()
    {
        _machine = new SwitchStateMachine(new SwitchOptions { MuteWhileSwitching = false }, _time);
        Send(new TakeRequested());

        Assert.Empty(Send(new PlaybackStarted()));
    }

    [Fact]
    public void Connexion_qui_echoue_rend_le_son_tout_de_suite()
    {
        VideoStarts();
        _time.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal([UnmuteSpeakers], Send(new EndpointDisconnected()));
    }

    [Fact]
    public void Relache_demandee_pendant_la_connexion_rend_le_son()
    {
        VideoStarts();

        Assert.Equal([PauseMedia, Disconnect, UnmuteSpeakers], Send(new ReleaseRequested()));
    }

    [Fact]
    public void Amener_sans_rien_jouer_ne_coupe_rien()
    {
        Assert.Equal([Reconnect], Send(new TakeRequested()));
    }

    [Fact]
    public void Reprise_apres_une_relache_coupe_aussi_les_haut_parleurs()
    {
        VideoStarts();
        Send(new EndpointConnected());
        After(1.1); // son rétabli
        Send(new PlaybackPaused());
        Assert.Equal([Disconnect], After(5.1));
        Send(new AirPodsSeen());
        Send(new PlaybackStarted()); // relance pendant la relâche

        Assert.Equal([Reconnect, MuteSpeakers], Send(new EndpointDisconnected()));
    }

    // ── Bascule suivie : la liaison radio dit si les AirPods arrivent (guide 4, test L, mesures du 2026-11-09) ──

    [Fact]
    public void Suivie_bascule_normale_mesuree_son_rendu_apres_l_arrivee()
    {
        Send(new RadioLinkDown());
        VideoStarts();
        _time.Advance(TimeSpan.FromSeconds(1.1));
        Assert.Empty(Send(new RadioLinkUp()));          // liaison à 1,1 s
        _time.Advance(TimeSpan.FromSeconds(0.8));
        Assert.Empty(Send(new EndpointConnected()));    // son dans les AirPods à 1,9 s

        Assert.Equal([UnmuteSpeakers], After(1.1));
    }

    [Fact]
    public void Suivie_bascule_lente_avec_le_micro_aucune_fuite_meme_au_dela_de_20_s()
    {
        // Liaison à 0,6 s puis le son seulement à 25 s : la limite fixe de 20 s aurait laissé fuir le son.
        Send(new RadioLinkDown());
        VideoStarts();
        _time.Advance(TimeSpan.FromSeconds(0.6));
        Send(new RadioLinkUp());
        for (int i = 0; i < 97; i++) Assert.Empty(After(0.25)); // jusqu'à 24,85 s : toujours coupé
        _time.Advance(TimeSpan.FromSeconds(0.15));
        Assert.Empty(Send(new EndpointConnected()));

        Assert.Equal([UnmuteSpeakers], After(1.1));
    }

    [Fact]
    public void Suivie_AirPods_injoignables_sans_liaison_son_rendu_et_bascule_abandonnee_a_6_s()
    {
        Send(new RadioLinkDown());
        VideoStarts();

        Assert.Empty(After(5.9));
        Assert.Equal([Disconnect, UnmuteSpeakers], After(0.2)); // essai annulé, son rendu
        Assert.Equal(SwitchState.Away, _machine.State);          // icône grise (recette S3)
        Assert.Equal([Reconnect, MuteSpeakers], Send(new TakeRequested())); // de nouveau possible (la vidéo joue : on recoupe)
    }

    [Fact]
    public void Suivie_liaison_perdue_en_route_son_rendu_tout_de_suite()
    {
        Send(new RadioLinkDown());
        VideoStarts();
        _time.Advance(TimeSpan.FromSeconds(1));
        Send(new RadioLinkUp());
        _time.Advance(TimeSpan.FromSeconds(2));

        Assert.Equal([Disconnect, UnmuteSpeakers], Send(new RadioLinkDown()));
        Assert.Equal(SwitchState.Away, _machine.State);
    }

    [Fact]
    public void Suivie_liaison_restee_de_la_relache_precedente_coupee_on_attend_la_nouvelle()
    {
        // Juste après une relâche, la liaison reste ~3 s (mesuré) : sa coupure n'est pas un échec.
        Send(new RadioLinkUp());
        VideoStarts();
        _time.Advance(TimeSpan.FromSeconds(2));
        Assert.Empty(Send(new RadioLinkDown())); // reste de la relâche : on attend 6 s à partir d'ici

        Assert.Empty(After(5.9));
        Assert.Equal([Disconnect, UnmuteSpeakers], After(0.2));
    }

    [Fact]
    public void Suivie_liaison_restee_puis_nouvelle_liaison_on_attend_l_arrivee()
    {
        Send(new RadioLinkUp());
        VideoStarts();
        _time.Advance(TimeSpan.FromSeconds(2));
        Send(new RadioLinkDown());
        _time.Advance(TimeSpan.FromSeconds(1));
        Send(new RadioLinkUp());                 // la nouvelle liaison

        for (int i = 0; i < 40; i++) Assert.Empty(After(0.25)); // 10 s de plus : toujours coupé
        Send(new EndpointConnected());
        Assert.Equal([UnmuteSpeakers], After(1.1));
    }

    [Fact]
    public void Suivie_liaison_etablie_mais_AirPods_jamais_arrives_son_rendu_a_la_fin_de_la_bascule()
    {
        Send(new RadioLinkDown());
        VideoStarts();
        _time.Advance(TimeSpan.FromSeconds(1));
        Send(new RadioLinkUp());

        Assert.Empty(After(28.9));                         // ni 6 s ni 20 s : la liaison dit qu'ils arrivent
        Assert.Equal([UnmuteSpeakers], After(0.2));        // 30 s : la bascule est abandonnée
        Assert.Equal(SwitchState.Away, _machine.State);
    }

    [Fact]
    public void Suivie_liaison_devenue_illisible_retour_a_la_limite_de_20_s()
    {
        Send(new RadioLinkDown());
        VideoStarts();
        _time.Advance(TimeSpan.FromSeconds(1));
        Send(new RadioLinkUp());
        _time.Advance(TimeSpan.FromSeconds(1));
        Send(new RadioLinkUnknown());

        Assert.Empty(After(17.9));
        Assert.Equal([UnmuteSpeakers], After(0.2)); // 20 s après la coupure
    }

    [Fact]
    public void Suivie_video_lancee_apres_l_abandon_ne_coupe_pas()
    {
        Send(new RadioLinkDown());
        Send(new TakeRequested());   // « Amener sur ce PC », boîtier fermé
        After(6.1);                  // pas de liaison : ils ne viendront pas

        Assert.Empty(Send(new PlaybackStarted())); // le son reste sur les haut-parleurs
    }

    [Fact]
    public void Suivie_la_liaison_hors_bascule_ne_declenche_rien()
    {
        Assert.Empty(Send(new RadioLinkUp()));
        Assert.Empty(Send(new RadioLinkDown()));
        Assert.Empty(Send(new RadioLinkUnknown()));
        Assert.Equal(SwitchState.Away, _machine.State);
    }

    [Fact]
    public void Option_desactivee_ne_coupe_jamais()
    {
        _machine = new SwitchStateMachine(new SwitchOptions { MuteWhileSwitching = false }, _time);

        Assert.Equal([Reconnect], VideoStarts());
    }
}
