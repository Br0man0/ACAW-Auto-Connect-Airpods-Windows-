using System.Runtime.InteropServices;
using Acaw.Core.Hosting;
using Windows.Devices.Bluetooth;

namespace Acaw.Windows.Bluetooth;

/// <summary>
/// Le port « liaison radio » de l'hôte : l'état de la liaison (ACL) avec les AirPods, poussé par WinRT
/// (BluetoothDevice.ConnectionStatusChanged). Mesuré identique à l'API Win32, à 50 ms près (guide 4, test L).
/// Si WinRT ne répond pas (Bluetooth coupé au lancement), l'état est « illisible » et l'appareil est recherché
/// de nouveau à la lecture suivante (l'hôte relit toutes les 30 s). Une adresse jamais appairée se lit « coupée » :
/// WinRT crée l'appareil au lieu de dire qu'il est introuvable (constaté le 2026-11-09).
/// </summary>
public sealed class WindowsRadioLink(string mac) : IRadioLink, IDisposable
{
    private readonly Lock _gate = new();
    private BluetoothDevice? _device;

    public event Action<LinkState>? StateChanged;

    public LinkState GetState()
    {
        try
        {
            var device = Resolve();
            if (device is null) return LinkState.Unknown;
            return device.ConnectionStatus == BluetoothConnectionStatus.Connected ? LinkState.Up : LinkState.Down;
        }
        catch (COMException)
        {
            Forget(); // Bluetooth coupé entre-temps : on recherchera l'appareil à la prochaine lecture
            return LinkState.Unknown;
        }
    }

    public void Dispose() => Forget();

    private BluetoothDevice? Resolve()
    {
        lock (_gate)
        {
            if (_device is not null) return _device;
            _device = BluetoothDevice.FromBluetoothAddressAsync(HandsFreeProfile.ParseAddress(mac)).AsTask().GetAwaiter().GetResult();
            if (_device is not null) _device.ConnectionStatusChanged += OnChanged;
            return _device;
        }
    }

    private void OnChanged(BluetoothDevice device, object _)
    {
        LinkState state;
        try
        {
            state = device.ConnectionStatus == BluetoothConnectionStatus.Connected ? LinkState.Up : LinkState.Down;
        }
        catch (COMException)
        {
            state = LinkState.Unknown;
        }
        StateChanged?.Invoke(state);
    }

    private void Forget()
    {
        lock (_gate)
        {
            if (_device is null) return;
            _device.ConnectionStatusChanged -= OnChanged;
            _device.Dispose();
            _device = null;
        }
    }
}
