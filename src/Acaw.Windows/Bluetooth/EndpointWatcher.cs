using Acaw.Core.Hosting;
using Windows.Win32.Foundation;
using Windows.Win32.Media.Audio;

namespace Acaw.Windows.Bluetooth;

/// <summary>
/// Prévient quand l'état d'un endpoint audio change (connecté, déconnecté…), sans avoir à le relire en boucle.
/// Windows appelle les méthodes ci-dessous depuis un de ses threads : on se contente de lever un événement.
/// Surtout, ne jamais rappeler l'API MMDevice depuis ces méthodes (risque de blocage, cf. ADR-0002).
/// </summary>
public sealed class EndpointWatcher : IMMNotificationClient, IDisposable
{
    private readonly IMMDeviceEnumerator _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
    private readonly string _endpointId;

    /// <summary>Levé avec le nouvel état de l'endpoint surveillé, depuis un thread de Windows.</summary>
    public event Action<HeadsetState>? StateChanged;

    public EndpointWatcher(string endpointId)
    {
        _endpointId = endpointId;
        _enumerator.RegisterEndpointNotificationCallback(this);
    }

    public void Dispose() => _enumerator.UnregisterEndpointNotificationCallback(this);

    void IMMNotificationClient.OnDeviceStateChanged(PCWSTR deviceId, DEVICE_STATE newState)
    {
        if (deviceId.ToString() == _endpointId) StateChanged?.Invoke(BluetoothAudio.ToHeadsetState(newState));
    }

    void IMMNotificationClient.OnDeviceAdded(PCWSTR deviceId) { }
    void IMMNotificationClient.OnDeviceRemoved(PCWSTR deviceId) { }
    void IMMNotificationClient.OnDefaultDeviceChanged(EDataFlow flow, ERole role, PCWSTR defaultDeviceId) { }
    void IMMNotificationClient.OnPropertyValueChanged(PCWSTR deviceId, PROPERTYKEY key) { }
}
