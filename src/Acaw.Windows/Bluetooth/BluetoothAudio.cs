using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Acaw.Core.Hosting;
using Windows.Win32;
using Windows.Win32.Devices.DeviceAndDriverInstallation;
using Windows.Win32.Foundation;
using Windows.Win32.Storage.FileSystem;
using Windows.Win32.Media.Audio;
using Windows.Win32.Media.KernelStreaming;
using Windows.Win32.System.Com;

namespace Acaw.Windows.Bluetooth;

/// <summary>Un endpoint audio Bluetooth vu par Windows, par ex. « Casque (AirPods Pro) ».</summary>
/// <param name="Id">Identifiant de l'endpoint (stable tant que l'appareil reste appairé).</param>
/// <param name="Name">Nom affiché dans les paramètres son (traduit, modifiable : ne pas s'y fier pour identifier).</param>
/// <param name="Mac">Adresse Bluetooth extraite du chemin du driver, ou null si introuvable.</param>
/// <param name="ModelId">Identifiant produit (« pid ») du chemin du driver ; chez Apple, c'est aussi le modèle annoncé en BLE.</param>
/// <param name="IsA2dp">true pour le profil musique (A2DP), false pour le mains libres (HFP).</param>
/// <param name="IsApple">true si le fabricant est Apple : AirPods, Beats… Seuls ceux-là émettent les annonces BLE qu'ACAW comprend.</param>
public sealed record BtEndpoint(string Id, string Name, string? Mac, string? ModelId, bool IsA2dp, bool IsApple);

/// <summary>
/// Accès à la pile audio Bluetooth de Windows : liste des endpoints, état, connexion et déconnexion par
/// KSPROPERTY_ONESHOT_RECONNECT / DISCONNECT (voir docs/adr/0001-mecanisme-bascule.md).
/// Repris du spike de la phase 0 (spikes/Phase0/BluetoothAudio.cs), où il a été mesuré.
/// </summary>
public static partial class BluetoothAudio
{
    // UUID de service Bluetooth « Audio Sink » (A2DP) tel qu'il apparaît dans le chemin du driver.
    private const string A2dpSinkUuid = "0000110b";

    // Dans le chemin du driver, l'adresse MAC apparaît sous la forme « &A0B1C2D3E4F5_C00000000 »…
    [GeneratedRegex("&([0-9A-F]{12})_C", RegexOptions.IgnoreCase)]
    private static partial Regex MacInPath();

    // … et l'identifiant produit sous la forme « _pid&2027# ».
    [GeneratedRegex("pid&([0-9A-F]{4})", RegexOptions.IgnoreCase)]
    private static partial Regex ModelInPath();

    // … et le fabricant sous la forme « _vid&0001004c » : 0001 = numéro attribué par le Bluetooth SIG,
    // 004c = Apple (le même identifiant que dans les annonces BLE). « 000205ac » = Apple par son numéro USB.
    [GeneratedRegex("vid&(0001004c|000205ac)", RegexOptions.IgnoreCase)]
    private static partial Regex AppleVendorInPath();

    private static readonly IMMDeviceEnumerator Enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();

    /// <summary>Liste les endpoints de sortie audio qui passent par un driver Bluetooth, connectés ou non.</summary>
    public static List<BtEndpoint> ListEndpoints()
    {
        var result = new List<BtEndpoint>();
        Enumerator.EnumAudioEndpoints(EDataFlow.eRender, (DEVICE_STATE)PInvoke.DEVICE_STATEMASK_ALL, out var collection);
        collection.GetCount(out uint count);

        for (uint i = 0; i < count; i++)
        {
            collection.Item(i, out var device);
            string? ksId = TryGetKsFilterId(device);
            if (ksId is null || !IsBluetooth(ksId)) continue;

            device.GetId(out PWSTR idPtr);
            var endpoint = FromDriverPath(Com.TakeString(idPtr), GetFriendlyName(device), ksId);
            // Le chemin du micro (HFP) ne contient pas la MAC : on la lit dans son parent (voir HandsFreeMac).
            if (endpoint.Mac is null && ksId.Contains("bthhfenum", StringComparison.OrdinalIgnoreCase))
                endpoint = endpoint with { Mac = HandsFreeMac(ksId) };
            result.Add(endpoint);
        }
        return result;
    }

    /// <summary>
    /// « {2}.\\?\bthhfenum#bthhfpaudio#8&amp;e451131&amp;d&amp;97#{6994…}\wave » → « bthhfenum\bthhfpaudio\8&amp;e451131&amp;d&amp;97 » :
    /// l'identifiant du périphérique du driver, tel que le connaît le gestionnaire de périphériques.
    /// </summary>
    public static string? InstanceIdFromPath(string ksFilterId)
    {
        int start = ksFilterId.IndexOf(@"\\?\", StringComparison.Ordinal);
        if (start < 0) return null;
        start += 4;
        int end = ksFilterId.IndexOf("#{", start, StringComparison.Ordinal);
        return end < 0 ? null : ksFilterId[start..end].Replace('#', '\\');
    }

    /// <summary>
    /// La MAC dans l'identifiant d'un périphérique Bluetooth, par ex. le parent du micro des AirPods :
    /// « BTHENUM\{0000111e-…}_VID&amp;0001004c_PID&amp;2027\7&amp;ad2957b&amp;0&amp;F478ACEA1566_C00000000 ».
    /// </summary>
    public static string? MacFromDeviceId(string deviceId)
    {
        var mac = MacInPath().Match(deviceId);
        return mac.Success ? FormatMac(mac.Groups[1].Value) : null;
    }

    // Le périphérique du micro (BTHHFENUM\BTHHFPAUDIO\…) a pour parent le service « Handsfree » des AirPods
    // (BTHENUM\{…111E…}…MAC…), relevé le 2026-11-09. CfgMgr, sans droits administrateur.
    private static string? HandsFreeMac(string ksFilterId) =>
        InstanceIdFromPath(ksFilterId) is { } instanceId ? ParentMac(instanceId) : null;

    /// <summary>La MAC lue dans le périphérique parent (null si introuvable). CfgMgr, sans droits administrateur.</summary>
    internal static unsafe string? ParentMac(string instanceId)
    {
        fixed (char* id = instanceId)
        {
            if (PInvoke.CM_Locate_DevNode(out uint node, id, CM_LOCATE_DEVNODE_FLAGS.CM_LOCATE_DEVNODE_NORMAL) != CONFIGRET.CR_SUCCESS)
                return null;
            if (PInvoke.CM_Get_Parent(out uint parent, node, 0) != CONFIGRET.CR_SUCCESS) return null;
            const int MaxDeviceIdLength = 200; // MAX_DEVICE_ID_LEN
            char* buffer = stackalloc char[MaxDeviceIdLength + 1];
            if (PInvoke.CM_Get_Device_IDW(parent, buffer, MaxDeviceIdLength + 1, 0) != CONFIGRET.CR_SUCCESS) return null;
            return MacFromDeviceId(new string(buffer));
        }
    }

    /// <summary>
    /// Décode le chemin du driver d'un endpoint Bluetooth (voir docs/guide/03-spike-phase0-explique.md) :
    /// adresse MAC, modèle, profil, fabricant. Séparé de la lecture COM pour être testé sans matériel.
    /// </summary>
    public static BtEndpoint FromDriverPath(string id, string name, string ksFilterId)
    {
        var mac = MacInPath().Match(ksFilterId);
        var model = ModelInPath().Match(ksFilterId);
        return new BtEndpoint(
            id,
            name,
            mac.Success ? FormatMac(mac.Groups[1].Value) : null,
            model.Success ? model.Groups[1].Value.ToUpperInvariant() : null,
            ksFilterId.Contains(A2dpSinkUuid, StringComparison.OrdinalIgnoreCase),
            AppleVendorInPath().IsMatch(ksFilterId));
    }

    /// <summary>true si le chemin du driver est celui d'un appareil Bluetooth.</summary>
    public static bool IsBluetooth(string ksFilterId) => ksFilterId.Contains("bth", StringComparison.OrdinalIgnoreCase);

    /// <summary>L'endpoint musique (A2DP) de l'appareil qui a cette MAC, ou null s'il n'est plus appairé.</summary>
    public static BtEndpoint? FindA2dp(string mac) =>
        ListEndpoints().FirstOrDefault(ep => ep.IsA2dp && string.Equals(ep.Mac, mac, StringComparison.OrdinalIgnoreCase));

    /// <summary>L'endpoint mains libres (HFP, le micro) de l'appareil qui a cette MAC, ou null si le profil est retiré.</summary>
    public static BtEndpoint? FindHandsFree(string mac) => HandsFreeOf(ListEndpoints(), mac);

    /// <summary>
    /// Relâche aussi le micro (HFP) : une relâche de la musique seule le laisse accroché au PC, et l'iPhone ne
    /// reprend pas les AirPods (guide 4, tests L3 et H9). L'ordre est envoyé directement au filtre du driver du
    /// micro (BTHHFENUM\BTHHFPAUDIO\…, dont le parent porte la MAC) : sa sortie audio, elle, n'est souvent pas
    /// encore visible au moment de la relâche (H9). Ouvrir ce filtre ne demande pas de droits administrateur.
    /// </summary>
    /// <returns>null si ces AirPods n'ont pas de micro (profil retiré) ; sinon le code Windows (0 = ordre envoyé).</returns>
    public static uint? ReleaseHandsFree(string mac) =>
        HandsFreeFilterFor(mac) is { } path ? SendToFilter(path, KSPROPERTY_BTAUDIO.KSPROPERTY_ONESHOT_DISCONNECT) : null;

    /// <summary>Le chemin du filtre du micro de ces AirPods, ou null si le profil mains libres est retiré.</summary>
    public static string? HandsFreeFilterFor(string mac) =>
        HandsFreeProfile.ListDeviceIds("BTHHFENUM")
            .Where(id => string.Equals(ParentMac(id), mac, StringComparison.OrdinalIgnoreCase))
            .Select(HandsFreeFilterPath)
            .FirstOrDefault();

    /// <summary>
    /// « BTHHFENUM\BTHHFPAUDIO\8&amp;E451131&amp;D&amp;97 » → « \\?\BTHHFENUM#BTHHFPAUDIO#8&amp;E451131&amp;D&amp;97#{6994ad04-…}\wave » :
    /// le chemin du filtre audio (catégorie KSCATEGORY_AUDIO, référence « wave »), relevé le 2026-11-09.
    /// </summary>
    public static string HandsFreeFilterPath(string instanceId) =>
        @"\\?\" + instanceId.Replace('\\', '#') + @"#{6994ad04-93ef-11d0-a3cc-00a0c9223196}\wave";

    // IOCTL_KS_PROPERTY = CTL_CODE(FILE_DEVICE_KS, 0, METHOD_NEITHER, FILE_ANY_ACCESS) : ce que fait IKsControl.
    private const uint IoctlKsProperty = 0x002F0003;
    private const uint GenericReadWrite = 0x80000000 | 0x40000000; // GENERIC_READ | GENERIC_WRITE

    private static unsafe uint SendToFilter(string path, KSPROPERTY_BTAUDIO command)
    {
        using var handle = PInvoke.CreateFile(path, GenericReadWrite,
            FILE_SHARE_MODE.FILE_SHARE_READ | FILE_SHARE_MODE.FILE_SHARE_WRITE, null,
            FILE_CREATION_DISPOSITION.OPEN_EXISTING, FILE_FLAGS_AND_ATTRIBUTES.FILE_ATTRIBUTE_NORMAL, null);
        if (handle.IsInvalid) return (uint)Marshal.GetLastWin32Error();

        var property = new KSIDENTIFIER();
        property.Set = typeof(KSPROPSETID_BtAudio).GUID;
        property.Id = (uint)command;
        property.Flags = PInvoke.KSPROPERTY_TYPE_GET;
        uint returned;
        bool ok = PInvoke.DeviceIoControl(new HANDLE(handle.DangerousGetHandle()), IoctlKsProperty, &property, (uint)sizeof(KSIDENTIFIER), null, 0, &returned, null);
        return ok ? 0 : (uint)Marshal.GetLastWin32Error();
    }

    /// <summary>La sélection seule, testable sans matériel : l'endpoint non-A2DP du même appareil.</summary>
    public static BtEndpoint? HandsFreeOf(IEnumerable<BtEndpoint> endpoints, string mac) =>
        endpoints.FirstOrDefault(ep => !ep.IsA2dp && string.Equals(ep.Mac, mac, StringComparison.OrdinalIgnoreCase));

    /// <summary>État actuel de l'endpoint.</summary>
    public static HeadsetState GetState(string endpointId)
    {
        Enumerator.GetDevice(endpointId, out var device);
        device.GetState(out var state);
        return ToHeadsetState(state);
    }

    /// <summary>Demande à Windows de connecter le casque. Rend la main tout de suite ; la radio travaille ensuite.</summary>
    public static void Connect(BtEndpoint endpoint) => SendOneShot(endpoint, KSPROPERTY_BTAUDIO.KSPROPERTY_ONESHOT_RECONNECT);

    /// <summary>Demande à Windows de déconnecter le casque.</summary>
    public static void Disconnect(BtEndpoint endpoint) => SendOneShot(endpoint, KSPROPERTY_BTAUDIO.KSPROPERTY_ONESHOT_DISCONNECT);

    internal static HeadsetState ToHeadsetState(DEVICE_STATE state) => state switch
    {
        DEVICE_STATE.DEVICE_STATE_ACTIVE => HeadsetState.Connected,
        DEVICE_STATE.DEVICE_STATE_UNPLUGGED => HeadsetState.Disconnected,
        _ => HeadsetState.Absent, // NOTPRESENT (Bluetooth coupé) ou DISABLED (désactivé à la main)
    };

    private static unsafe void SendOneShot(BtEndpoint endpoint, KSPROPERTY_BTAUDIO command)
    {
        var ks = OpenKsControl(endpoint.Id);
        var property = new KSIDENTIFIER();
        property.Set = typeof(KSPROPSETID_BtAudio).GUID;
        property.Id = (uint)command;
        property.Flags = PInvoke.KSPROPERTY_TYPE_GET;
        ks.KsProperty(in property, (uint)Unsafe.SizeOf<KSIDENTIFIER>(), null, 0, out _);
    }

    /// <summary>
    /// endpoint → connecteur → connecteur du driver → topologie du driver → périphérique du driver → IKsControl.
    /// IPart.Activate ne fournit pas IKsControl (E_NOINTERFACE) : il faut passer par IMMDevice.Activate
    /// sur le périphérique du filtre, comme le fait l'outil ToothTray.
    /// </summary>
    private static IKsControl OpenKsControl(string endpointId)
    {
        Enumerator.GetDevice(endpointId, out var device);
        var part = (IPart)GetDriverConnector(device);
        part.GetTopologyObject(out var driverTopology);
        driverTopology.GetDeviceId(out PWSTR driverIdPtr);
        Enumerator.GetDevice(Com.TakeString(driverIdPtr), out var driverDevice);
        driverDevice.Activate(typeof(IKsControl).GUID, CLSCTX.CLSCTX_ALL, null, out object ks);
        return (IKsControl)ks;
    }

    private static IConnector GetDriverConnector(IMMDevice device)
    {
        device.Activate(typeof(IDeviceTopology).GUID, CLSCTX.CLSCTX_ALL, null, out object topologyObj);
        ((IDeviceTopology)topologyObj).GetConnector(0, out var connector);
        connector.GetConnectedTo(out var connectedTo);
        return connectedTo;
    }

    private static string? TryGetKsFilterId(IMMDevice device)
    {
        try
        {
            device.Activate(typeof(IDeviceTopology).GUID, CLSCTX.CLSCTX_ALL, null, out object topologyObj);
            ((IDeviceTopology)topologyObj).GetConnector(0, out var connector);
            connector.GetDeviceIdConnectedTo(out PWSTR ksIdPtr);
            return Com.TakeString(ksIdPtr);
        }
        catch (Exception e) when (Com.IsComFailure(e))
        {
            return null; // endpoint désactivé, virtuel ou sans topologie lisible : pas un casque Bluetooth
        }
    }

    private static string GetFriendlyName(IMMDevice device)
    {
        try
        {
            device.OpenPropertyStore(STGM.STGM_READ, out var store);
            store.GetValue(PInvoke.PKEY_Device_FriendlyName, out var value);
            try
            {
                PInvoke.PropVariantToStringAlloc(in value, out PWSTR namePtr).ThrowOnFailure();
                return Com.TakeString(namePtr);
            }
            finally
            {
                PInvoke.PropVariantClear(ref value);
            }
        }
        catch (Exception e) when (Com.IsComFailure(e))
        {
            return Acaw.Core.Config.PairedHeadset.UnreadableName;
        }
    }

    private static string FormatMac(string hex12) =>
        string.Join(':', Enumerable.Range(0, 6).Select(i => hex12.Substring(i * 2, 2))).ToUpperInvariant();
}
