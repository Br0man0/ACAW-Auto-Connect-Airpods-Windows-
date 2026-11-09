using Windows.Win32;
using Windows.Win32.Devices.DeviceAndDriverInstallation;
using Windows.Win32.Foundation;

namespace Acaw.Windows.Bluetooth;

/// <summary>
/// Le profil « mains libres » (HFP) des AirPods est-il actif ? Tant qu'il l'est, chaque bascule prend 10 à
/// 13 s au lieu de ~2 s (ADR-0001). Windows crée un périphérique pour chaque service Bluetooth d'un appareil
/// (BTHENUM\{uuid du service}_…{adresse}) ; celui du service « Handsfree » (0x111E) disparaît quand on décoche
/// « Mains libres téléphonie » dans les propriétés des AirPods.
/// </summary>
public static class HandsFreeProfile
{
    /// <summary>UUID Bluetooth du service « Handsfree » (profil mains libres, côté casque).</summary>
    public const string HandsFreeService = "{0000111E-0000-1000-8000-00805F9B34FB}";

    private const uint FilterEnumerator = 0x00000001; // CM_GETIDLIST_FILTER_ENUMERATOR : seulement BTHENUM
    private const uint FilterPresent = 0x00000100;    // CM_GETIDLIST_FILTER_PRESENT : seulement les présents

    /// <summary>true si le profil mains libres est actif pour l'appareil qui a cette adresse.</summary>
    public static bool IsEnabled(string mac) => IsEnabledIn(ListBluetoothDeviceIds(), mac);

    /// <summary>La décision seule, sur une liste d'identifiants de périphériques : testable sans matériel.</summary>
    public static bool IsEnabledIn(IEnumerable<string> deviceInstanceIds, string mac)
    {
        string address = mac.Replace(":", "", StringComparison.Ordinal);
        return deviceInstanceIds.Any(id => id.StartsWith(@"BTHENUM\" + HandsFreeService, StringComparison.OrdinalIgnoreCase)
                                           && id.Contains(address, StringComparison.OrdinalIgnoreCase));
    }

    // Les identifiants des périphériques Bluetooth présents, lus avec CfgMgr (sans droits administrateur).
    private static unsafe List<string> ListBluetoothDeviceIds()
    {
        const uint flags = FilterEnumerator | FilterPresent;
        for (int attempt = 0; attempt < 3; attempt++) // la liste peut grandir entre les deux appels
        {
            if (PInvoke.CM_Get_Device_ID_List_Size(out uint length, "BTHENUM", flags) != CONFIGRET.CR_SUCCESS) return [];
            var buffer = new char[length];
            fixed (char* p = buffer)
            {
                var result = PInvoke.CM_Get_Device_ID_List("BTHENUM", new PZZWSTR(p), length, flags);
                if (result == CONFIGRET.CR_BUFFER_SMALL) continue;
                if (result != CONFIGRET.CR_SUCCESS) return [];
            }
            // Format « liste double-zéro » : des chaînes séparées par \0, terminées par \0\0.
            return new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries).ToList();
        }
        return [];
    }
}
