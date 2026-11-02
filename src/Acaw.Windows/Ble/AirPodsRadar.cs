using Acaw.Core.Hosting;

namespace Acaw.Windows.Ble;

/// <summary>
/// Le port « écoute BLE » de l'hôte. Un nouvel écouteur Windows est créé à chaque démarrage : après une
/// erreur (Bluetooth coupé puis rallumé), c'est le moyen le plus sûr de repartir proprement.
/// </summary>
public sealed class AirPodsRadar(ushort model) : IAirPodsRadar, IDisposable
{
    private BleScanner? _scanner;

    public event Action? AirPodsSeen;
    public event Action? Stopped;

    public void Start()
    {
        Stop();
        var scanner = new BleScanner(model);
        scanner.FrameReceived += _ => AirPodsSeen?.Invoke();
        scanner.Stopped += () => Stopped?.Invoke();
        scanner.Start(); // lève une exception si le Bluetooth est coupé : l'hôte réessaiera
        _scanner = scanner;
    }

    public void Stop()
    {
        _scanner?.Dispose();
        _scanner = null;
    }

    public void Dispose() => Stop();
}
