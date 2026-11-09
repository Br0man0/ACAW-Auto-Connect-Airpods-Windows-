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

    [Fact]
    public void Option_desactivee_ne_coupe_jamais()
    {
        _machine = new SwitchStateMachine(new SwitchOptions { MuteWhileSwitching = false }, _time);

        Assert.Equal([Reconnect], VideoStarts());
    }
}
