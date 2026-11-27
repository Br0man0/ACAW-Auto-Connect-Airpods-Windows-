using Acaw.Bluetooth.Takeover;
using Microsoft.Win32;
using Windows.Win32;
using Windows.Win32.Devices.DeviceAndDriverInstallation;

namespace Acaw.Service;

/// <summary>
/// Le ménage dans les nœuds de périphériques autour d'un changement d'adaptateur (les règles sont dans
/// <see cref="DeviceNodes"/>).
/// </summary>
/// <remarks>
/// Quand l'adaptateur passe sur WinUSB, l'adaptateur virtuel reçoit ses propres nœuds pour les mêmes appareils.
/// Les anciens, cachés, font numéroter les nouveaux (« Casque (2- AirPods Pro) ») : on oublie les nœuds
/// <b>audio</b> cachés, jamais ceux des appareils eux-mêmes (leur appairage pourrait sauter). Au retour, même
/// ménage du côté de l'adaptateur virtuel.
/// </remarks>
internal static unsafe class DeviceCleanup
{
    private const string EnumKey = @"SYSTEM\CurrentControlSet\Enum";
    private const string PairedDevicesKey = @"SYSTEM\CurrentControlSet\Services\BTHPORT\Parameters\Devices";

    /// <summary>Oublie les nœuds audio cachés sous <paramref name="rootInstanceId"/>. Rend leur nombre.</summary>
    public static int ForgetHiddenAudioNodes(string rootInstanceId)
    {
        using RegistryKey? root = Registry.LocalMachine.OpenSubKey(EnumKey);
        if (root is null) return 0;
        var all = new List<string>();
        var prefixes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string enumerator in root.GetSubKeyNames())
        {
            using RegistryKey? e = root.OpenSubKey(enumerator);
            foreach (string device in e?.GetSubKeyNames() ?? [])
            {
                using RegistryKey? d = e!.OpenSubKey(device);
                foreach (string instance in d?.GetSubKeyNames() ?? [])
                {
                    string id = $@"{enumerator}\{device}\{instance}";
                    all.Add(id);
                    using RegistryKey? node = d!.OpenSubKey(instance);
                    if (node?.GetValue("ParentIdPrefix") is string prefix) prefixes[id] = prefix;
                }
            }
        }
        int removed = 0;
        foreach (string id in DeviceNodes.CollectSubtree(rootInstanceId, all, id => prefixes.GetValueOrDefault(id), audioOnly: true))
        {
            // Caché seulement : la recherche normale échoue et la recherche « fantôme » réussit. Jamais un nœud présent.
            fixed (char* p = id)
            {
                uint dev;
                if (PInvoke.CM_Locate_DevNode(&dev, p, CM_LOCATE_DEVNODE_FLAGS.CM_LOCATE_DEVNODE_NORMAL) != CONFIGRET.CR_SUCCESS
                    && PInvoke.CM_Locate_DevNode(&dev, p, CM_LOCATE_DEVNODE_FLAGS.CM_LOCATE_DEVNODE_PHANTOM) == CONFIGRET.CR_SUCCESS
                    && PInvoke.CM_Uninstall_DevNode(dev, 0) == CONFIGRET.CR_SUCCESS)
                {
                    removed++;
                }
            }
        }
        return removed;
    }

    /// <summary>
    /// Les autres instances du même modèle USB que l'adaptateur réel : celles de l'adaptateur virtuel (même VID et PID,
    /// autre numéro de série).
    /// </summary>
    public static List<string> VirtualTwins(string realInstanceId)
    {
        string model = realInstanceId[..realInstanceId.LastIndexOf('\\')];
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey($@"{EnumKey}\{model}");
        return [.. (key?.GetSubKeyNames() ?? []).Select(i => $@"{model}\{i}")
                    .Where(id => !id.Equals(realInstanceId, StringComparison.OrdinalIgnoreCase))];
    }

    /// <summary>
    /// Recopie dans l'appairage l'identifiant des périphériques Bluetooth LE (la manette Xbox), que Windows n'y range
    /// que pour l'adaptateur d'origine. Sans lui, leur partie HID ne démarre pas sur l'adaptateur virtuel.
    /// Rend le nombre de périphériques complétés.
    /// </summary>
    public static int CarryLeDeviceIds()
    {
        try
        {
            using RegistryKey? nodes = Registry.LocalMachine.OpenSubKey($@"{EnumKey}\BTHLEDevice");
            using RegistryKey? paired = Registry.LocalMachine.OpenSubKey(PairedDevicesKey, writable: true);
            if (nodes is null || paired is null) return 0;
            int carried = 0;
            foreach (string name in nodes.GetSubKeyNames())
            {
                if (!DeviceNodes.TryParseLeDeviceId(name, out LeDeviceId id)) continue;
                using RegistryKey? device = paired.OpenSubKey(id.Address.ToLowerInvariant(), writable: true);
                if (device is null || device.GetValue("VID") is not null) continue;
                device.SetValue("VIDType", id.Source, RegistryValueKind.DWord);
                device.SetValue("VID", id.Vid, RegistryValueKind.DWord);
                device.SetValue("PID", id.Pid, RegistryValueKind.DWord);
                device.SetValue("Version", id.Revision, RegistryValueKind.DWord);
                carried++;
            }
            return carried;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            ServiceLog.Warn("nœuds", $"identifiants LE non recopiés ({e.Message}) : la manette Xbox pourrait ne pas marcher");
            return 0;
        }
    }
}
