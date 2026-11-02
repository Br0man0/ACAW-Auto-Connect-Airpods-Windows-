using Acaw.Core.Switching;
using Microsoft.Extensions.Time.Testing;
using static Acaw.Core.Switching.SwitchCommand;

namespace Acaw.Core.Tests;

/// <summary>
/// Règle R7 : pendant que les AirPods arrivent, le son ne doit pas sortir des haut-parleurs. On les coupe,
/// et on les rétablit dans TOUS les cas : arrivée des AirPods, échec, abandon, ou au plus tard après 6 s.
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
    public void AirPods_qui_tardent_le_son_revient_sur_les_haut_parleurs_apres_6_s()
    {
        VideoStarts();

        Assert.Empty(After(5.9));
        Assert.Equal([UnmuteSpeakers], After(0.2));
        Assert.Equal(SwitchState.Switching, _machine.State); // la connexion peut encore réussir
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
