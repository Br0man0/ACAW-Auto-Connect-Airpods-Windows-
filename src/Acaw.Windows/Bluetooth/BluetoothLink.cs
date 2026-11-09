using Windows.Win32;
using Windows.Win32.Devices.Bluetooth;

namespace Acaw.Windows.Bluetooth;

/// <summary>
/// La liaison radio (ACL) entre le PC et les AirPods, distincte de la sortie audio : c'est la première étape
/// d'une connexion, avant le profil musique (A2DP). Sert à l'étape 0 de mesure de « bascule suivie » : la liaison
/// s'établit-elle nettement avant la sortie audio ? (docs/guide/04-protocole-test-phase0.md, test L)
/// </summary>
public static class BluetoothLink
{
    /// <summary>
    /// La liaison vue par l'API Win32 (BluetoothGetDeviceInfo → fConnected), sans droits administrateur.
    /// null si le Bluetooth est coupé ou si l'appareil n'est pas appairé.
    /// </summary>
    public static unsafe bool? IsConnected(string mac)
    {
        var search = new BLUETOOTH_FIND_RADIO_PARAMS { dwSize = (uint)sizeof(BLUETOOTH_FIND_RADIO_PARAMS) };
        using var find = PInvoke.BluetoothFindFirstRadio(search, out var radio);
        using (radio)
        {
            if (find.IsInvalid || radio.IsInvalid) return null;
            var device = new BLUETOOTH_DEVICE_INFO { dwSize = (uint)sizeof(BLUETOOTH_DEVICE_INFO) };
            device.Address.ullLong = HandsFreeProfile.ParseAddress(mac);
            if (PInvoke.BluetoothGetDeviceInfo(radio, ref device) != 0) return null;
            return device.fConnected;
        }
    }
}
