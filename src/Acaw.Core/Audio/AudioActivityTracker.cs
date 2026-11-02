using Acaw.Core.Switching;

namespace Acaw.Core.Audio;

/// <summary>
/// Transforme deux signaux bruts du PC en événements pour la machine à états :
/// <list type="bullet">
/// <item>« un lecteur média est en lecture » (contrôles média Windows : YouTube, Spotify…), fiable et immédiat ;</item>
/// <item>le volume crête des applications, relevé régulièrement, pour les sons sans lecteur (jeux, Discord).</item>
/// </list>
/// Un son seul doit durer <see cref="SwitchOptions.SoundConfirmDelay"/> pour compter : une notification ne
/// déclenche donc rien. Même règle de threading que la machine : un seul thread l'appelle.
/// </summary>
public sealed class AudioActivityTracker(SwitchOptions options, TimeProvider time)
{
    // En dessous de ce volume crête (−60 dB), on considère qu'il n'y a pas de son.
    private const float AudibleThreshold = 0.001f;

    private static readonly SwitchEvent[] None = [];

    private bool _media;                    // un lecteur média est en lecture
    private bool _sound;                    // un son hors lecteur est confirmé
    private DateTimeOffset? _soundSince;    // début d'un son pas encore confirmé
    private DateTimeOffset? _silenceSince;  // début d'un silence pendant un son confirmé

    /// <summary>true si le PC est considéré comme en train de jouer.</summary>
    public bool Active => _media || _sound;

    /// <summary>À appeler quand l'état « au moins un lecteur média en lecture » change.</summary>
    public IReadOnlyList<SwitchEvent> OnMediaPlaying(bool playing)
    {
        if (playing == _media) return None;
        bool wasActive = Active;
        _media = playing;
        // Pendant qu'un média joue, son propre son ne doit pas être compté à part : sinon sa mise en pause
        // ne serait pas vue comme une pause, mais comme un silence (relâche après 30 s au lieu de 5).
        ResetSound();

        if (!wasActive && Active) return [new PlaybackStarted()];
        if (wasActive && !Active) return [new PlaybackPaused()];
        return None;
    }

    /// <summary>À appeler à intervalle régulier avec le volume crête des applications (0 à 1).</summary>
    public IReadOnlyList<SwitchEvent> OnPeak(float peak)
    {
        if (_media) return None; // le lecteur média fait foi tant qu'il joue

        bool wasActive = Active;
        var now = time.GetUtcNow();
        if (peak > AudibleThreshold)
        {
            _silenceSince = null;
            _soundSince ??= now;
            if (!_sound && now - _soundSince >= options.SoundConfirmDelay) _sound = true;
        }
        else
        {
            _soundSince = null;
            _silenceSince ??= now;
            if (_sound && now - _silenceSince >= options.SilenceConfirmDelay) _sound = false;
        }

        if (!wasActive && Active) return [new PlaybackStarted()];
        if (wasActive && !Active) return [new SilenceDetected()];
        return None;
    }

    private void ResetSound()
    {
        _sound = false;
        _soundSince = null;
        _silenceSince = null;
    }
}
