using Windows.Win32;
using Windows.Win32.Devices.Bluetooth;
using Windows.Win32.Devices.DeviceAndDriverInstallation;
using Windows.Win32.Foundation;

namespace Acaw.Windows.Bluetooth;

/// <summary>Le résultat, et le code brut de Windows (à noter dans le journal en cas d'échec).</summary>
public readonly record struct HandsFreeOutcome(HandsFreeResult Result, uint Code)
{
    public bool Succeeded => Result is HandsFreeResult.Done or HandsFreeResult.AlreadyInState;
}

/// <summary>Résultat d'une tentative d'activer ou de retirer le profil mains libres.</summary>
public enum HandsFreeResult
{
    Done,
    AlreadyInState,
    AccessDenied,
    NoRadio,
    NotPaired,
    NotSupported,
    Failed,
}

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
    public static bool IsEnabled(string mac) => IsEnabledIn(ListDeviceIds("BTHENUM"), mac);

    /// <summary>
    /// Active ou retire le profil mains libres, comme la case « Mains libres téléphonie » des propriétés
    /// Bluetooth (BluetoothSetServiceState). Sans droits administrateur sur le compte de Romsy (vérifié le
    /// 2026-11-09). Prend ~3 s : à appeler hors du thread de l'interface. Pendant ce temps, Windows coupe le
    /// lien deux fois quelques dixièmes de seconde : l'hôte doit être prévenu (AcawHost.BeginProfileChange).
    /// </summary>
    public static unsafe HandsFreeOutcome SetEnabled(string mac, bool enabled)
    {
        var search = new BLUETOOTH_FIND_RADIO_PARAMS { dwSize = (uint)sizeof(BLUETOOTH_FIND_RADIO_PARAMS) };
        using var find = PInvoke.BluetoothFindFirstRadio(search, out var radio);
        using (radio)
        {
            // La radio d'abord : Bluetooth coupé, les AirPods ne sont plus « présents » et le profil paraîtrait
            // retiré à tort.
            if (find.IsInvalid || radio.IsInvalid) return new(HandsFreeResult.NoRadio, 0);

            // Déjà dans l'état voulu : on ne touche à rien (et on évite les coupures du lien pour rien).
            if (IsEnabled(mac) == enabled) return new(HandsFreeResult.AlreadyInState, 0);

            var device = new BLUETOOTH_DEVICE_INFO { dwSize = (uint)sizeof(BLUETOOTH_DEVICE_INFO) };
            device.Address.ullLong = ParseAddress(mac);
            uint found = PInvoke.BluetoothGetDeviceInfo(radio, ref device);
            if (found != 0) return new(HandsFreeResult.NotPaired, found);

            Guid service = new(HandsFreeService.Trim('{', '}'));
            uint flags = enabled ? PInvoke.BLUETOOTH_SERVICE_ENABLE : PInvoke.BLUETOOTH_SERVICE_DISABLE;
            uint code = PInvoke.BluetoothSetServiceState(radio, device, service, flags);
            return new(ToResult(code, enabled), code);
        }
    }

    /// <summary>« A1:B2:C3:D4:E5:F6 » → 0xA1B2C3D4E5F6, le format attendu par les API Bluetooth de Windows.</summary>
    public static ulong ParseAddress(string mac) =>
        Convert.ToUInt64(mac.Replace(":", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal), 16);

    /// <summary>
    /// Traduit le code de retour de BluetoothSetServiceState. La documentation annonce E_INVALIDARG pour « déjà
    /// dans cet état » ; en pratique, retirer un profil déjà absent renvoie ERROR_NOT_FOUND (mesuré le 2026-11-09).
    /// </summary>
    public static HandsFreeResult ToResult(uint code, bool enabling) => code switch
    {
        0 => HandsFreeResult.Done,                          // ERROR_SUCCESS
        0x80070057 => HandsFreeResult.AlreadyInState,       // E_INVALIDARG : déjà activé, ou déjà retiré (doc)
        1168 when !enabling => HandsFreeResult.AlreadyInState, // ERROR_NOT_FOUND : le profil n'est déjà plus là
        5 => HandsFreeResult.AccessDenied,                  // ERROR_ACCESS_DENIED
        1060 => HandsFreeResult.NotSupported,               // ERROR_SERVICE_DOES_NOT_EXIST
        _ => HandsFreeResult.Failed,
    };

    /// <summary>Le message à montrer à l'utilisateur après une tentative.</summary>
    public static string Describe(HandsFreeResult result, bool enabled) => result switch
    {
        HandsFreeResult.Done or HandsFreeResult.AlreadyInState => enabled
            ? "Micro des AirPods activé : il marche pour les appels, mais chaque bascule prend ~10 s."
            : "Micro des AirPods retiré : bascules rapides (~2 s).",
        HandsFreeResult.AccessDenied =>
            "Windows refuse ce changement sans droits administrateur. Passe par les propriétés Bluetooth des AirPods (control printers → AirPods → Propriétés → Services).",
        HandsFreeResult.NoRadio => "Bluetooth du PC coupé : active-le, puis réessaie.",
        HandsFreeResult.NotPaired => "Ces AirPods ne sont plus appairés à ce PC.",
        HandsFreeResult.NotSupported => "Ces AirPods ne proposent pas le profil mains libres.",
        _ => "Changement du micro impossible : Windows a renvoyé une erreur.",
    };

    /// <summary>La décision seule, sur une liste d'identifiants de périphériques : testable sans matériel.</summary>
    public static bool IsEnabledIn(IEnumerable<string> deviceInstanceIds, string mac)
    {
        string address = mac.Replace(":", "", StringComparison.Ordinal);
        return deviceInstanceIds.Any(id => id.StartsWith(@"BTHENUM\" + HandsFreeService, StringComparison.OrdinalIgnoreCase)
                                           && id.Contains(address, StringComparison.OrdinalIgnoreCase));
    }

    // Les identifiants des périphériques présents créés par cet énumérateur (BTHENUM : services Bluetooth ;
    // BTHHFENUM : micros mains libres), lus avec CfgMgr (sans droits administrateur).
    internal static unsafe List<string> ListDeviceIds(string enumerator)
    {
        const uint flags = FilterEnumerator | FilterPresent;
        for (int attempt = 0; attempt < 3; attempt++) // la liste peut grandir entre les deux appels
        {
            if (PInvoke.CM_Get_Device_ID_List_Size(out uint length, enumerator, flags) != CONFIGRET.CR_SUCCESS) return [];
            var buffer = new char[length];
            fixed (char* p = buffer)
            {
                var result = PInvoke.CM_Get_Device_ID_List(enumerator, new PZZWSTR(p), length, flags);
                if (result == CONFIGRET.CR_BUFFER_SMALL) continue;
                if (result != CONFIGRET.CR_SUCCESS) return [];
            }
            // Format « liste double-zéro » : des chaînes séparées par \0, terminées par \0\0.
            return new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries).ToList();
        }
        return [];
    }
}
