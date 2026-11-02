using Acaw.Core.Audio;
using Acaw.Core.Switching;
using Microsoft.Extensions.Time.Testing;

namespace Acaw.Core.Tests;

/// <summary>
/// Le tracker reçoit l'état des lecteurs média et le volume crête toutes les 250 ms, comme dans l'hôte.
/// Les helpers simulent « N secondes de son » ou « N secondes de silence ».
/// </summary>
public class AudioActivityTrackerTests
{
    private const float Loud = 0.3f;
    private const float Quiet = 0f;

    private readonly FakeTimeProvider _time = new();
    private readonly AudioActivityTracker _tracker;

    public AudioActivityTrackerTests() => _tracker = new AudioActivityTracker(new SwitchOptions(), _time);

    // Envoie des relevés de volume pendant `seconds`, toutes les 250 ms, et renvoie les événements produits.
    private List<SwitchEvent> Peaks(float peak, double seconds)
    {
        var events = new List<SwitchEvent>();
        for (int i = 0; i < seconds * 4; i++)
        {
            events.AddRange(_tracker.OnPeak(peak));
            _time.Advance(TimeSpan.FromMilliseconds(250));
        }
        return events;
    }

    [Fact]
    public void Lecteur_media_qui_demarre_declenche_tout_de_suite()
    {
        Assert.IsType<PlaybackStarted>(Assert.Single(_tracker.OnMediaPlaying(true)));
    }

    [Fact]
    public void Lecteur_media_mis_en_pause_est_une_pause_explicite()
    {
        _tracker.OnMediaPlaying(true);

        Assert.IsType<PlaybackPaused>(Assert.Single(_tracker.OnMediaPlaying(false)));
    }

    [Fact]
    public void Notification_courte_ne_declenche_rien()
    {
        var events = Peaks(Loud, 1.5); // un « ding » Discord
        events.AddRange(Peaks(Quiet, 5));

        Assert.Empty(events);
    }

    [Fact]
    public void Son_de_jeu_qui_dure_declenche_apres_2_s()
    {
        Assert.Empty(Peaks(Loud, 1.9));
        Assert.IsType<PlaybackStarted>(Assert.Single(Peaks(Loud, 0.5)));
    }

    [Fact]
    public void Fin_d_un_son_de_jeu_est_un_silence_et_non_une_pause()
    {
        Peaks(Loud, 3);

        Assert.Empty(Peaks(Quiet, 1.9));
        Assert.IsType<SilenceDetected>(Assert.Single(Peaks(Quiet, 0.5)));
    }

    [Fact]
    public void Courte_pause_dans_un_son_de_jeu_ne_coupe_rien()
    {
        Peaks(Loud, 3);

        var events = Peaks(Quiet, 1.5); // blanc entre deux répliques
        events.AddRange(Peaks(Loud, 3));

        Assert.Empty(events);
    }

    [Fact]
    public void Son_du_media_lui_meme_ne_masque_pas_sa_mise_en_pause()
    {
        // YouTube joue : le contrôle média dit « lecture » ET le volume est haut.
        _tracker.OnMediaPlaying(true);
        Peaks(Loud, 5);

        // À la pause, on doit voir une pause (relâche en 5 s), pas attendre un silence (30 s).
        Assert.IsType<PlaybackPaused>(Assert.Single(_tracker.OnMediaPlaying(false)));
    }

    [Fact]
    public void Media_en_pause_avec_un_autre_son_qui_continue_redemarre_2_s_plus_tard()
    {
        _tracker.OnMediaPlaying(true);
        Assert.IsType<PlaybackPaused>(Assert.Single(_tracker.OnMediaPlaying(false)));

        // Un jeu continue de jouer : il est reconfirmé au bout de 2 s, avant la relâche prévue à 5 s.
        Assert.IsType<PlaybackStarted>(Assert.Single(Peaks(Loud, 2.5)));
    }

    [Fact]
    public void Media_qui_demarre_pendant_un_son_de_jeu_ne_redeclenche_pas()
    {
        Peaks(Loud, 3);

        Assert.Empty(_tracker.OnMediaPlaying(true));
        Assert.True(_tracker.Active);
    }

    [Fact]
    public void Etat_repete_ne_produit_rien()
    {
        _tracker.OnMediaPlaying(true);
        Assert.Empty(_tracker.OnMediaPlaying(true));

        _tracker.OnMediaPlaying(false);
        Assert.Empty(_tracker.OnMediaPlaying(false));
    }
}
