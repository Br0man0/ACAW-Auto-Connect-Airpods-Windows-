using Acaw.Core.Hosting;

namespace Acaw.Core.Tests;

// Faux ports : ils remplacent Windows dans les tests de l'hôte. Chacun note ce qu'on lui demande,
// et le test déclenche à la main ce que Windows ferait (« le casque est maintenant connecté »…).

internal sealed class FakeHeadset : IHeadsetPort
{
    public HeadsetState State { get; set; } = HeadsetState.Disconnected;
    public int Connects { get; private set; }
    public int Disconnects { get; private set; }
    public Exception? FailWith { get; set; }

    public event Action<HeadsetState>? StateChanged;

    public HeadsetState GetState() => State;

    public void Connect()
    {
        Connects++;
        if (FailWith is not null) throw FailWith;
    }

    public void Disconnect()
    {
        Disconnects++;
        if (FailWith is not null) throw FailWith;
    }

    /// <summary>Simule Windows qui signale un nouvel état.</summary>
    public void Becomes(HeadsetState state)
    {
        State = state;
        StateChanged?.Invoke(state);
    }
}

internal sealed class FakeMedia : IMediaPort
{
    public bool IsPlaying { get; private set; }
    public int Pauses { get; private set; }

    public event Action<bool>? PlayingChanged;

    /// <summary>Comme le vrai adaptateur : la mise en pause se traduit par un changement d'état.</summary>
    public void PauseAll()
    {
        Pauses++;
        Plays(false);
    }

    public void Plays(bool playing)
    {
        if (IsPlaying == playing) return;
        IsPlaying = playing;
        PlayingChanged?.Invoke(playing);
    }

    /// <summary>Simule un message arrivé en retard : il annonce une valeur qui n'est plus la vraie.</summary>
    public void SendsStale(bool announced) => PlayingChanged?.Invoke(announced);
}

internal sealed class FakeMeter : ISoundMeter
{
    public float Peak { get; set; }

    public float ReadPeak() => Peak;
}

internal sealed class FakeSpeakers : ISpeakers
{
    public bool Muted { get; private set; }
    public int Mutes { get; private set; }
    public int Unmutes { get; private set; }

    public void Mute()
    {
        Mutes++;
        Muted = true;
    }

    public void Unmute()
    {
        Unmutes++;
        Muted = false;
    }
}

internal sealed class FakeRadar : IAirPodsRadar
{
    public int Starts { get; private set; }
    public bool Running { get; private set; }
    public bool FailOnStart { get; set; }

    public event Action? AirPodsSeen;
    public event Action? Stopped;

    public void Start()
    {
        Starts++;
        if (FailOnStart) throw new InvalidOperationException("Bluetooth coupé");
        Running = true;
    }

    public void Stop() => Running = false;

    public void Sees() => AirPodsSeen?.Invoke();

    public void Dies()
    {
        Running = false;
        Stopped?.Invoke();
    }
}
