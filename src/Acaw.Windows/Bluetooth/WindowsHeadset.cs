using Acaw.Core.Hosting;

namespace Acaw.Windows.Bluetooth;

/// <summary>
/// Le port « casque » de l'hôte, branché sur Windows. L'endpoint des AirPods est retrouvé une fois par sa
/// MAC puis gardé en mémoire (revue de phase 1, point 5) ; on le recherche à nouveau seulement s'il devient
/// inaccessible (Bluetooth coupé, AirPods ré-appairés…). Utilisé uniquement depuis le thread de l'hôte.
/// </summary>
public sealed class WindowsHeadset(string mac) : IHeadsetPort, IDisposable
{
    private BtEndpoint? _endpoint;
    private EndpointWatcher? _watcher;

    public event Action<HeadsetState>? StateChanged;

    public HeadsetState GetState()
    {
        try
        {
            var endpoint = Resolve();
            if (endpoint is null) return HeadsetState.Absent;
            var state = BluetoothAudio.GetState(endpoint.Id);
            if (state == HeadsetState.Absent) Forget();
            return state;
        }
        catch (Exception e) when (Com.IsComFailure(e))
        {
            Forget(); // l'endpoint a disparu : on le recherchera au prochain appel
            return HeadsetState.Absent;
        }
    }

    public void Connect() => BluetoothAudio.Connect(Resolve() ?? throw NotFound());

    public void Disconnect() => BluetoothAudio.Disconnect(Resolve() ?? throw NotFound());

    public void Dispose() => Forget();

    private BtEndpoint? Resolve()
    {
        if (_endpoint is not null) return _endpoint;
        _endpoint = BluetoothAudio.FindA2dp(mac);
        if (_endpoint is not null)
        {
            _watcher = new EndpointWatcher(_endpoint.Id);
            _watcher.StateChanged += state => StateChanged?.Invoke(state);
        }
        return _endpoint;
    }

    private void Forget()
    {
        _watcher?.Dispose();
        _watcher = null;
        _endpoint = null;
    }

    private static InvalidOperationException NotFound() =>
        new("AirPods introuvables : Bluetooth coupé, ou AirPods plus appairés à ce PC.");
}
