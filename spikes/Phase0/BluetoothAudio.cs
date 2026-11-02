using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Media.Audio;
using Windows.Win32.Media.KernelStreaming;
using Windows.Win32.System.Com;

namespace PodHop.Spike;

/// <summary>Un endpoint audio Bluetooth vu par Windows, par ex. « Écouteurs (AirPods Pro) ».</summary>
/// <param name="Id">Identifiant de l'endpoint (stable tant que l'appareil reste appairé).</param>
/// <param name="Name">Nom affiché dans les paramètres son (traduit, modifiable : ne pas s'y fier pour identifier).</param>
/// <param name="Mac">Adresse Bluetooth extraite du chemin du driver, ou null si introuvable.</param>
/// <param name="ModelId">Identifiant produit (« pid ») du chemin du driver, ex. « 2027 ». Chez Apple, c'est
/// aussi le modèle annoncé en BLE : il permet de reconnaître les AirPods appairés parmi les annonces.</param>
/// <param name="IsA2dp">true pour le profil musique (A2DP), false pour le mains libres (HFP).</param>
/// <param name="KsFilterId">Chemin brut du filtre KS, affiché pour le diagnostic.</param>
internal sealed record BtEndpoint(string Id, string Name, string? Mac, string? ModelId, bool IsA2dp, string KsFilterId);

/// <summary>
/// Accès à la pile audio Bluetooth de Windows : liste des endpoints, état, et commandes
/// KSPROPERTY_ONESHOT_RECONNECT / DISCONNECT (voir docs/adr/0001-mecanisme-bascule.md).
/// </summary>
internal static partial class BluetoothAudio
{
    // UUID de service Bluetooth « Audio Sink » (A2DP) tel qu'il apparaît dans le chemin du driver.
    private const string A2dpSinkUuid = "0000110b";

    // Dans le chemin du driver, l'adresse MAC apparaît sous la forme « &A0B1C2D3E4F5_C00000000 ».
    [GeneratedRegex("&([0-9A-F]{12})_C", RegexOptions.IgnoreCase)]
    private static partial Regex MacInPath();

    // … et l'identifiant produit sous la forme « _pid&2027# ».
    [GeneratedRegex("pid&([0-9A-F]{4})", RegexOptions.IgnoreCase)]
    private static partial Regex ModelInPath();

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
            if (ksId is null || !ksId.Contains("bth", StringComparison.OrdinalIgnoreCase))
                continue; // pas un appareil Bluetooth

            device.GetId(out PWSTR idPtr);
            string id = TakeString(idPtr);
            var mac = MacInPath().Match(ksId);
            var model = ModelInPath().Match(ksId);
            result.Add(new BtEndpoint(
                id,
                GetFriendlyName(device),
                mac.Success ? FormatMac(mac.Groups[1].Value) : null,
                model.Success ? model.Groups[1].Value.ToUpperInvariant() : null,
                ksId.Contains(A2dpSinkUuid, StringComparison.OrdinalIgnoreCase),
                ksId));
        }
        return result;
    }

    /// <summary>État actuel de l'endpoint (ACTIVE = connecté et utilisable).</summary>
    public static DEVICE_STATE GetState(string endpointId)
    {
        Enumerator.GetDevice(endpointId, out var device);
        device.GetState(out var state);
        return state;
    }

    /// <summary>
    /// Demande à Windows de connecter (ou déconnecter) le casque. L'appel rend la main tout de suite :
    /// la connexion radio se fait ensuite, en arrière-plan. Il faut surveiller GetState pour savoir quand c'est fini.
    /// </summary>
    public static unsafe void SendOneShot(BtEndpoint endpoint, KSPROPERTY_BTAUDIO command)
    {
        var ks = OpenKsControl(endpoint.Id);
        var property = new KSIDENTIFIER();
        property.Set = typeof(KSPROPSETID_BtAudio).GUID;
        property.Id = (uint)command;
        property.Flags = PInvoke.KSPROPERTY_TYPE_GET;
        ks.KsProperty(in property, (uint)Unsafe.SizeOf<KSIDENTIFIER>(), null, 0, out _);
    }

    /// <summary>
    /// Remonte la topologie audio jusqu'au filtre KS du driver Bluetooth, puis lui demande IKsControl :
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
        Enumerator.GetDevice(TakeString(driverIdPtr), out var driverDevice);
        driverDevice.Activate(typeof(IKsControl).GUID, CLSCTX.CLSCTX_ALL, null, out object ks);
        return (IKsControl)ks;
    }

    private static IConnector GetDriverConnector(IMMDevice device)
    {
        device.Activate(typeof(IDeviceTopology).GUID, CLSCTX.CLSCTX_ALL, null, out object topologyObj);
        var topology = (IDeviceTopology)topologyObj;
        topology.GetConnector(0, out var connector);
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
            return TakeString(ksIdPtr);
        }
        catch (COMException)
        {
            return null; // endpoint désactivé ou sans topologie lisible
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
                return TakeString(namePtr);
            }
            finally
            {
                PInvoke.PropVariantClear(ref value);
            }
        }
        catch (COMException)
        {
            return "(nom illisible)";
        }
    }

    /// <summary>Copie une chaîne allouée par Windows puis libère la mémoire (sinon : fuite).</summary>
    private static unsafe string TakeString(PWSTR ptr)
    {
        try { return ptr.ToString(); }
        finally { Marshal.FreeCoTaskMem((nint)ptr.Value); }
    }

    private static string FormatMac(string hex12) =>
        string.Join(':', Enumerable.Range(0, 6).Select(i => hex12.Substring(i * 2, 2))).ToUpperInvariant();
}
