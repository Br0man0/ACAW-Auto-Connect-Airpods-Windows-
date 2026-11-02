using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Devices.Bluetooth.Advertisement;

namespace PodHop.Spike;

/// <summary>
/// Trame « proximity pairing » (type 0x07) diffusée en BLE par les AirPods.
/// Format : 01 | modèle(2, petit-boutiste) | statut | batteries | charge+boîtier | couvercle | couleur | suffixe | 16 octets chiffrés
/// Signification des bits établie au test C1 de la phase 0 : voir docs/protocole-ble-airpods.md.
/// </summary>
internal sealed record ProximityFrame(ulong Address, short Rssi, byte[] Value)
{
    /// <summary>Même valeur que le « pid » du driver Bluetooth classique (ex. 2027).</summary>
    public string Model => $"{Value[2]:X2}{Value[1]:X2}";
    public byte Status => Value[3];
    public byte Batteries => Value[4];
    public byte ChargeAndCase => Value[5];
    public byte Lid => Value[6];
    public byte Color => Value[7];

    // Bits 1 et 3 : un écouteur dans l'oreille chacun. On les compte sans dire lequel est le gauche :
    // l'attribution dépend de l'écouteur qui émet et n'est pas encore fiable.
    public int PodsInEar => BitOperations.PopCount((uint)(Status & 0x0A));

    // Bit 2 : les deux écouteurs dans le boîtier. Bit 4 : au moins un.
    public int PodsInCase => (Status & 0x04) != 0 ? 2 : (Status & 0x10) != 0 ? 1 : 0;

    /// <summary>Partie en clair : sert à n'afficher une trame que lorsqu'elle change.</summary>
    public string ClearPartKey => $"{Address:X12}:{Convert.ToHexString(Value, 0, 9)}";

    public override string ToString() =>
        $"{DateTime.Now:HH:mm:ss.fff}  ble={Address:X12}  rssi={Rssi,4}  modèle={Model}  " +
        $"oreille={PodsInEar} boîtier={PodsInCase}  " +
        $"statut={Bits(Status)}  écouteurs={Pct(Batteries >> 4)}/{Pct(Batteries & 0xF)}  " +
        $"charge={Bits((byte)(ChargeAndCase >> 4), 4)} boîtier%={Pct(ChargeAndCase & 0xF)}  " +
        $"couvercle={Bits(Lid)}  couleur={Color:X2}";

    // Niveau de batterie sur 0..10 (×10 %), 15 = inconnu (écouteur absent ou dans le boîtier fermé).
    private static string Pct(int nibble) => nibble <= 10 ? $"{nibble * 10}%" : "—";
    private static string Bits(byte b, int width = 8) => Convert.ToString(b, 2).PadLeft(width, '0');
}

/// <summary>Écoute passive des annonces BLE d'Apple (identifiant fabricant 0x004C).</summary>
internal static class AppleBle
{
    private const ushort AppleCompanyId = 0x004C;
    private const byte ProximityPairingType = 0x07;

    public static BluetoothLEAdvertisementWatcher Start(Action<ProximityFrame> onFrame)
    {
        // Passif : on écoute sans interroger les appareils, ce qui consomme moins de radio.
        var watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Passive };
        watcher.Received += (_, args) =>
        {
            foreach (var data in args.Advertisement.ManufacturerData)
            {
                if (data.CompanyId != AppleCompanyId) continue;
                var frame = TryParse(data.Data.ToArray(), args.BluetoothAddress, args.RawSignalStrengthInDBm);
                if (frame is not null) onFrame(frame);
            }
        };
        watcher.Stopped += (_, args) =>
        {
            if (args.Error != Windows.Devices.Bluetooth.BluetoothError.Success)
                Console.WriteLine($"[BLE] écoute arrêtée : {args.Error} (Bluetooth coupé ?)");
        };
        watcher.Start();
        return watcher;
    }

    /// <summary>Les données Apple sont une suite de blocs [type][longueur][valeur] ; on cherche le type 0x07.</summary>
    private static ProximityFrame? TryParse(byte[] payload, ulong address, short rssi)
    {
        int i = 0;
        while (i + 2 <= payload.Length)
        {
            byte type = payload[i];
            int length = payload[i + 1];
            if (i + 2 + length > payload.Length) return null;
            if (type == ProximityPairingType && length >= 9)
                return new ProximityFrame(address, rssi, payload[(i + 2)..(i + 2 + length)]);
            i += 2 + length;
        }
        return null;
    }
}
