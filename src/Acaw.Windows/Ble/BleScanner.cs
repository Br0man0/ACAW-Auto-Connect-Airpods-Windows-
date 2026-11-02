using System.Runtime.InteropServices.WindowsRuntime;
using Acaw.Core.Ble;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;

namespace Acaw.Windows.Ble;

/// <summary>
/// Écoute passive des annonces BLE d'Apple, décodées par le cœur et filtrées sur le modèle des AirPods
/// configurés (ce qui écarte les autres appareils Apple à proximité).
/// </summary>
public sealed class BleScanner : IDisposable
{
    private readonly BluetoothLEAdvertisementWatcher _watcher = new() { ScanningMode = BluetoothLEScanningMode.Passive };

    /// <summary>Levé à chaque trame des AirPods, depuis un thread de Windows.</summary>
    public event Action<ProximityFrame>? FrameReceived;

    /// <summary>Levé si l'écoute s'arrête d'elle-même sur une erreur (Bluetooth coupé…).</summary>
    public event Action? Stopped;

    public BleScanner(ushort model)
    {
        _watcher.Received += (_, args) =>
        {
            foreach (var data in args.Advertisement.ManufacturerData)
            {
                if (data.CompanyId != AppleAdvertisementParser.AppleCompanyId) continue;
                var frame = AppleAdvertisementParser.TryParse(data.Data.ToArray());
                if (frame is not null && frame.Model == model) FrameReceived?.Invoke(frame);
            }
        };
        _watcher.Stopped += (_, args) =>
        {
            if (args.Error != BluetoothError.Success) Stopped?.Invoke();
        };
    }

    public void Start() => _watcher.Start();

    public void Dispose() => _watcher.Stop();
}
