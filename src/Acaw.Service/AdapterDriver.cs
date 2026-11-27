using System.ComponentModel;
using System.Runtime.InteropServices;
using Acaw.Bluetooth.Takeover;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Devices.DeviceAndDriverInstallation;
using Windows.Win32.Foundation;
using Windows.Win32.Storage.FileSystem;

namespace Acaw.Service;

/// <summary>Le pilote actuel d'un périphérique, pour le journal (lu dans le registre).</summary>
internal sealed record DriverInfo(string? Service, string? Description, string? Provider, string? Version)
{
    public override string ToString() =>
        $"« {Description ?? "?"} » ({Provider ?? "?"} {Version ?? "?"}, service {Service ?? "aucun"})";
}

/// <summary>
/// Trouve l'adaptateur Bluetooth et le fait passer de son pilote d'origine (Intel…) au pilote générique WinUSB
/// de Windows, et inversement. Toutes ces opérations exigent les droits administrateur.
/// </summary>
/// <remarks>
/// Le changement de pilote suit ce que fait le Gestionnaire de périphériques avec « Mettre à jour le pilote →
/// Choisir parmi une liste » : SetupAPI construit la liste des pilotes possibles, on en choisit un, puis
/// <c>DiInstallDevice</c> l'installe. Pour rendre, on demande à Windows le meilleur pilote compatible, qui est
/// celui d'origine.
/// </remarks>
internal static unsafe class AdapterDriver
{
    private const string EnumKey = @"SYSTEM\CurrentControlSet\Enum";
    private const string ClassKey = @"SYSTEM\CurrentControlSet\Control\Class";

    // Interface publiée par chaque radio Bluetooth (GUID_BTHPORT_DEVICE_INTERFACE).
    private static readonly Guid RadioInterface = new("0850302A-B344-4FDA-9BE9-90576B8D46F0");
    // Classe d'installation « périphériques USB » : celle de WinUSB (GUID_DEVCLASS_USBDEVICE).
    private static readonly Guid UsbDeviceClass = new("88BAE032-5A81-49F0-BC3D-A4FF138216D6");
    // Interface publiée par chaque hub USB (GUID_DEVINTERFACE_USB_HUB).
    private static readonly Guid HubInterface = new("F18A0E88-C30C-11D0-8815-00A0C906BED8");

    /// <summary>Raison donnée par <see cref="Find"/> quand aucun adaptateur ne convient.</summary>
    public enum NotFoundReason { None, NoAdapter, NotUsb, Composite }

    /// <summary>
    /// L'adaptateur à prendre : celui déjà retenu s'il est branché, sinon une radio Bluetooth USB entière qui
    /// n'est pas l'adaptateur virtuel d'usbip-win2. Null si aucune, avec la raison.
    /// </summary>
    public static string? Find(out NotFoundReason reason)
    {
        reason = NotFoundReason.None;
        if (ServiceState.RadioInstanceId is { } stored && IsPresent(stored)) return stored;
        // Un adaptateur déjà passé sur WinUSB n'est plus listé comme radio : on le retrouve par l'état retenu.
        foreach (string bound in ServiceState.BoundInstances)
        {
            if (IsPresent(bound) && IsOnWinUsb(bound)) return bound;
        }
        reason = NotFoundReason.NoAdapter;
        foreach (string path in InterfacePaths(RadioInterface, deviceId: null))
        {
            if (DeviceNodes.InstanceIdFromInterfacePath(path) is not { } id) continue;
            if (!DeviceNodes.IsUsb(id))
            {
                reason = NotFoundReason.NotUsb;
                continue;
            }
            if (DeviceNodes.IsCompositeFunction(id))
            {
                reason = NotFoundReason.Composite;
                continue;
            }
            if (IsVirtual(id)) continue;
            reason = NotFoundReason.None;
            return id;
        }
        return null;
    }

    /// <summary>true si le périphérique est branché (présent pour Windows).</summary>
    public static bool IsPresent(string instanceId) => Locate(instanceId, CM_LOCATE_DEVNODE_FLAGS.CM_LOCATE_DEVNODE_NORMAL) is not null;

    /// <summary>
    /// true si le pilote Bluetooth tourne vraiment sur ce périphérique : il a publié son interface de radio.
    /// Le registre change dès l'installation ; seule cette interface prouve que le pilote a démarré.
    /// </summary>
    public static bool IsRadioRunning(string instanceId) => InterfacePaths(RadioInterface, instanceId).Count > 0;

    /// <summary>true si le périphérique a WinUSB pour pilote (lu dans le registre : vrai même débranché).</summary>
    public static bool IsOnWinUsb(string instanceId) =>
        string.Equals(Driver(instanceId).Service, "WinUSB", StringComparison.OrdinalIgnoreCase);

    /// <summary>Le pilote actuel du périphérique.</summary>
    public static DriverInfo Driver(string instanceId)
    {
        using RegistryKey? device = Registry.LocalMachine.OpenSubKey($@"{EnumKey}\{instanceId}");
        string? service = device?.GetValue("Service") as string;
        if (device?.GetValue("Driver") is not string driverKey) return new DriverInfo(service, null, null, null);
        using RegistryKey? driver = Registry.LocalMachine.OpenSubKey($@"{ClassKey}\{driverKey}");
        return new DriverInfo(service, driver?.GetValue("DriverDesc") as string, driver?.GetValue("ProviderName") as string,
                              driver?.GetValue("DriverVersion") as string);
    }

    /// <summary>true si un nouveau pilote est installé mais pas encore démarré (le périphérique était ouvert).</summary>
    public static bool NeedsRestart(string instanceId) =>
        Locate(instanceId, CM_LOCATE_DEVNODE_FLAGS.CM_LOCATE_DEVNODE_NORMAL) is { } dev
        && PInvoke.CM_Get_DevNode_Status(out CM_DEVNODE_STATUS_FLAGS status, out _, dev, 0) == CONFIGRET.CR_SUCCESS
        && status.HasFlag(CM_DEVNODE_STATUS_FLAGS.DN_NEED_RESTART);

    /// <summary>Installe le pilote générique WinUSB. Rend true si Windows demande un redémarrage.</summary>
    public static bool BindWinUsb(string instanceId)
    {
        ServiceLog.Info("pilote", $"passage sur WinUSB : {instanceId}");
        return Install(instanceId, winUsb: true);
    }

    /// <summary>Réinstalle le meilleur pilote compatible (celui d'origine). Rend true si Windows demande un redémarrage.</summary>
    public static bool RestoreNative(string instanceId)
    {
        ServiceLog.Info("pilote", $"retour au pilote d'origine : {instanceId}");
        return Install(instanceId, winUsb: false);
    }

    /// <summary>
    /// Débranche puis rebranche le périphérique par logiciel (port du hub USB), pour qu'il démarre sur le pilote
    /// installé même si quelque chose le tenait ouvert. false si le hub a refusé.
    /// </summary>
    public static bool CyclePort(string instanceId)
    {
        const uint IoctlUsbHubCyclePort = 0x00220444;
        if (Locate(instanceId, CM_LOCATE_DEVNODE_FLAGS.CM_LOCATE_DEVNODE_NORMAL) is not { } dev
            || PInvoke.CM_Get_Parent(out uint hub, dev, 0) != CONFIGRET.CR_SUCCESS
            || DeviceId(hub) is not { } hubId)
        {
            return false;
        }
        uint port = 0;
        uint size = sizeof(uint);
        const uint CmDrpAddress = 0x1D; // CM_DRP_ADDRESS : le numéro de port sur le hub
        if (PInvoke.CM_Get_DevNode_Registry_Property(dev, CmDrpAddress, null, &port, &size, 0) != CONFIGRET.CR_SUCCESS || port == 0)
            return false;
        foreach (string path in InterfacePaths(HubInterface, hubId))
        {
            using SafeFileHandle handle = PInvoke.CreateFile(path, 0x40000000 /* GENERIC_WRITE */,
                FILE_SHARE_MODE.FILE_SHARE_READ | FILE_SHARE_MODE.FILE_SHARE_WRITE, null, FILE_CREATION_DISPOSITION.OPEN_EXISTING,
                0, null);
            if (handle.IsInvalid) continue;
            // USB_CYCLE_PORT_PARAMS : ConnectionIndex (le port), StatusReturned.
            Span<byte> parameters = stackalloc byte[8];
            MemoryMarshal.Write(parameters, port);
            bool ok = PInvoke.DeviceIoControl(handle, IoctlUsbHubCyclePort, parameters, parameters, out _, null);
            ServiceLog.Info("pilote", $"rebranchement de {instanceId} (port {port} du hub {hubId}) : "
                                      + (ok ? "fait" : $"refusé, erreur Win32 {Marshal.GetLastPInvokeError()}"));
            return ok;
        }
        return false;
    }

    /// <summary>
    /// Attend qu'une condition devienne vraie, en la vérifiant toutes les 250 ms. false si le délai est dépassé.
    /// </summary>
    public static bool WaitFor(Func<bool> condition, TimeSpan timeout, CancellationToken cancel = default)
    {
        DateTime end = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow >= end || cancel.IsCancellationRequested) return false;
            Thread.Sleep(250);
        }
        return true;
    }

    // L'adaptateur virtuel d'usbip-win2 est sous son contrôleur USB virtuel (service usbip2_ude).
    private static bool IsVirtual(string instanceId)
    {
        if (Locate(instanceId, CM_LOCATE_DEVNODE_FLAGS.CM_LOCATE_DEVNODE_NORMAL) is not { } dev) return false;
        for (int i = 0; i < 4 && PInvoke.CM_Get_Parent(out uint parent, dev, 0) == CONFIGRET.CR_SUCCESS; i++, dev = parent)
        {
            if (DeviceId(parent) is { } parentId
                && string.Equals(Driver(parentId).Service, "usbip2_ude", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool Install(string instanceId, bool winUsb)
    {
        HDEVINFO set = PInvoke.SetupDiCreateDeviceInfoList((Guid*)null, HWND.Null);
        if (set == HDEVINFO.Null || set.Value == -1) throw new Win32Exception();
        try
        {
            var dev = new SP_DEVINFO_DATA { cbSize = (uint)sizeof(SP_DEVINFO_DATA) };
            fixed (char* id = instanceId)
            {
                if (!PInvoke.SetupDiOpenDeviceInfo(set, id, HWND.Null, 0, &dev)) throw new Win32Exception();
            }
            var parameters = new SP_DEVINSTALL_PARAMS_W { cbSize = (uint)Marshal.SizeOf<SP_DEVINSTALL_PARAMS_W>() };
            if (!PInvoke.SetupDiGetDeviceInstallParams(set, &dev, ref parameters)) throw new Win32Exception();

            SETUP_DI_DRIVER_TYPE type = SETUP_DI_DRIVER_TYPE.SPDIT_COMPATDRIVER;
            if (winUsb)
            {
                // Seulement le fichier winusb.inf de Windows, y compris ses pilotes « exclus » de la recherche normale.
                parameters.Flags |= SETUP_DI_DEVICE_INSTALL_FLAGS.DI_ENUMSINGLEINF;
                parameters.FlagsEx |= SETUP_DI_DEVICE_INSTALL_FLAGS_EX.DI_FLAGSEX_ALLOWEXCLUDEDDRVS;
                string inf = Environment.ExpandEnvironmentVariables(@"%SystemRoot%\INF\winusb.inf");
                Span<char> driverPath = parameters.DriverPath.AsSpan();
                driverPath.Clear();
                inf.AsSpan().CopyTo(driverPath);
                type = SETUP_DI_DRIVER_TYPE.SPDIT_CLASSDRIVER;
                // WinUSB est un pilote de la classe « périphériques USB », l'adaptateur est de classe « Bluetooth » :
                // on change d'abord sa classe, comme le Gestionnaire de périphériques. Le pilote d'origine
                // remettra la sienne au retour.
                byte[] classGuid = System.Text.Encoding.Unicode.GetBytes(UsbDeviceClass.ToString("B") + "\0");
                fixed (byte* g = classGuid)
                {
                    if (!PInvoke.SetupDiSetDeviceRegistryProperty(set, &dev, SETUP_DI_REGISTRY_PROPERTY.SPDRP_CLASSGUID, g, (uint)classGuid.Length))
                        throw new Win32Exception();
                }
                dev.ClassGuid = UsbDeviceClass;
            }
            if (!PInvoke.SetupDiSetDeviceInstallParams(set, &dev, in parameters) || !PInvoke.SetupDiBuildDriverInfoList(set, &dev, type))
                throw new Win32Exception();

            if (winUsb)
            {
                SP_DRVINFO_DATA_V2_W? pick = null;
                for (uint i = 0; ; i++)
                {
                    var candidate = new SP_DRVINFO_DATA_V2_W { cbSize = (uint)sizeof(SP_DRVINFO_DATA_V2_W) };
                    if (!PInvoke.SetupDiEnumDriverInfo(set, &dev, type, i, &candidate)) break;
                    if (Text(candidate.Description.AsSpan()).Contains("WinUsb", StringComparison.OrdinalIgnoreCase))
                    {
                        pick = candidate;
                        break;
                    }
                }
                if (pick is not { } selected) throw new InvalidOperationException("winusb.inf ne propose pas de pilote « WinUsb ».");
                if (!PInvoke.SetupDiSetSelectedDriver(set, &dev, &selected)) throw new Win32Exception();
            }
            else if (!PInvoke.SetupDiCallClassInstaller(DI_FUNCTION.DIF_SELECTBESTCOMPATDRV, set, &dev))
            {
                throw new Win32Exception();
            }

            var chosen = new SP_DRVINFO_DATA_V2_W { cbSize = (uint)sizeof(SP_DRVINFO_DATA_V2_W) };
            BOOL needReboot;
            if (!PInvoke.SetupDiGetSelectedDriver(set, &dev, &chosen) || !PInvoke.DiInstallDevice(HWND.Null, set, &dev, &chosen, 0, &needReboot))
                throw new Win32Exception();
            ServiceLog.Info("pilote", $"installé : « {Text(chosen.Description.AsSpan())} » sur {instanceId}"
                                      + (needReboot ? " (Windows demande un redémarrage)" : ""));
            return needReboot;
        }
        finally
        {
            PInvoke.SetupDiDestroyDeviceInfoList(set);
        }
    }

    private static string Text(ReadOnlySpan<char> buffer)
    {
        int end = buffer.IndexOf('\0');
        return new string(end < 0 ? buffer : buffer[..end]);
    }

    private static uint? Locate(string instanceId, CM_LOCATE_DEVNODE_FLAGS flags)
    {
        fixed (char* id = instanceId)
        {
            uint dev;
            return PInvoke.CM_Locate_DevNode(&dev, id, flags) == CONFIGRET.CR_SUCCESS ? dev : null;
        }
    }

    private static string? DeviceId(uint dev)
    {
        Span<char> buffer = stackalloc char[400];
        return PInvoke.CM_Get_Device_IDW(dev, buffer, 0) == CONFIGRET.CR_SUCCESS ? Text(buffer) : null;
    }

    // Les chemins des interfaces présentes d'une classe, éventuellement d'un seul périphérique.
    private static List<string> InterfacePaths(Guid interfaceClass, string? deviceId)
    {
        var paths = new List<string>();
        fixed (char* device = deviceId)
        {
            uint length;
            if (PInvoke.CM_Get_Device_Interface_List_Size(&length, &interfaceClass, device,
                    CM_GET_DEVICE_INTERFACE_LIST_FLAGS.CM_GET_DEVICE_INTERFACE_LIST_PRESENT) != CONFIGRET.CR_SUCCESS || length <= 1)
                return paths;
            var buffer = new char[length];
            fixed (char* b = buffer)
            {
                if (PInvoke.CM_Get_Device_Interface_List(&interfaceClass, device, b, length,
                        CM_GET_DEVICE_INTERFACE_LIST_FLAGS.CM_GET_DEVICE_INTERFACE_LIST_PRESENT) != CONFIGRET.CR_SUCCESS)
                    return paths;
            }
            // Liste de chaînes séparées par \0, terminée par un \0 de plus.
            foreach (string path in new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries)) paths.Add(path);
        }
        return paths;
    }
}
