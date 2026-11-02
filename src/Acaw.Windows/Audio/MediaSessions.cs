using Acaw.Core.Hosting;
using Windows.Media.Control;

namespace Acaw.Windows.Audio;

/// <summary>
/// Le port « lecteurs média » de l'hôte, branché sur les contrôles média de Windows (SMTC) : ce que
/// montre le panneau du volume pour YouTube, Spotify, VLC… Les événements arrivent sur des threads
/// de Windows : l'état est donc protégé par un verrou.
/// </summary>
public sealed class MediaSessions : IMediaPort, IDisposable
{
    private readonly GlobalSystemMediaTransportControlsSessionManager _manager;
    private readonly Lock _gate = new();
    private List<GlobalSystemMediaTransportControlsSession> _sessions = [];
    private bool _playing;

    private MediaSessions(GlobalSystemMediaTransportControlsSessionManager manager)
    {
        _manager = manager;
        _manager.SessionsChanged += OnSessionsChanged;
        Refresh();
    }

    public static async Task<MediaSessions> CreateAsync() =>
        new(await GlobalSystemMediaTransportControlsSessionManager.RequestAsync());

    public bool IsPlaying
    {
        get { lock (_gate) return _playing; }
    }

    public event Action<bool>? PlayingChanged;

    public void PauseAll()
    {
        List<GlobalSystemMediaTransportControlsSession> sessions;
        lock (_gate) sessions = [.. _sessions];
        foreach (var session in sessions.Where(IsSessionPlaying))
            _ = session.TryPauseAsync(); // on n'attend pas : la pause se verra par PlaybackInfoChanged
    }

    public void Dispose()
    {
        _manager.SessionsChanged -= OnSessionsChanged;
        lock (_gate)
        {
            foreach (var session in _sessions) session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
            _sessions = [];
        }
    }

    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args) =>
        Refresh();

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) =>
        Recompute();

    // Une appli ouverte ou fermée : on se réabonne à toutes les sessions.
    private void Refresh()
    {
        lock (_gate)
        {
            foreach (var session in _sessions) session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
            _sessions = [.. _manager.GetSessions()];
            foreach (var session in _sessions) session.PlaybackInfoChanged += OnPlaybackInfoChanged;
        }
        Recompute();
    }

    private void Recompute()
    {
        bool playing;
        lock (_gate)
        {
            playing = _sessions.Any(IsSessionPlaying);
            if (playing == _playing) return;
            _playing = playing;
        }
        PlayingChanged?.Invoke(playing); // hors du verrou : l'abonné peut faire ce qu'il veut
    }

    private static bool IsSessionPlaying(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            return session.GetPlaybackInfo()?.PlaybackStatus
                   == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        }
        catch (Exception)
        {
            return false; // l'appli vient de se fermer
        }
    }
}
