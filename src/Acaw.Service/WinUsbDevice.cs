using System.ComponentModel;
using System.Runtime.InteropServices;
using Acaw.Bluetooth.Takeover;
using Acaw.Bluetooth.Usb;
using Acaw.Bluetooth.UsbIp;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Devices.Usb;
using Windows.Win32.Storage.FileSystem;

namespace Acaw.Service;

/// <summary>
/// L'adaptateur Bluetooth ouvert par le pilote générique WinUSB : on lui parle en USB brut.
/// </summary>
/// <remarks>
/// WinUSB donne une « poignée » par interface : la première à l'ouverture, les suivantes par
/// <c>WinUsb_GetAssociatedInterface</c>, dans l'ordre des numéros d'interface. Chaque point de terminaison
/// appartient à l'interface dont le réglage alternatif <b>actuel</b> le contient : la table est refaite à chaque
/// SET_INTERFACE. Les transferts sont synchrones, un fil par tuyau (voir <see cref="RadioRelay"/>), et rendent
/// une erreur Win32 (0 = réussi) au lieu de lancer une exception. Les transferts isochrones arrivent en W1-4.
/// </remarks>
internal sealed unsafe class WinUsbDevice : IDisposable
{
    private readonly SafeFileHandle _file;
    private readonly List<WinUsb_FreeSafeHandle> _interfaces;
    private readonly byte[] _interfaceNumbers;   // numéro d'interface de chaque poignée, dans l'ordre WinUSB
    private readonly byte[] _alternates;         // réglage alternatif actuel de chaque interface
    private readonly Lock _mapLock = new();
    private Dictionary<byte, int> _pipes = [];   // point de terminaison → index de l'interface ; remplacée en bloc

    private WinUsbDevice(SafeFileHandle file, List<WinUsb_FreeSafeHandle> interfaces, byte[] device, byte[] configuration,
                         UsbSpeed speed)
    {
        _file = file;
        _interfaces = interfaces;
        DeviceDescriptor = device;
        ConfigurationDescriptor = configuration;
        Configuration = UsbConfiguration.Parse(configuration);
        Speed = speed;
        _interfaceNumbers = [.. Configuration.Settings.Select(s => s.Number).Distinct().Order()];
        _alternates = new byte[_interfaceNumbers.Length];
        MapPipes();
    }

    /// <summary>Le descripteur de périphérique tel que l'adaptateur le donne (18 octets).</summary>
    public byte[] DeviceDescriptor { get; }

    /// <summary>Le descripteur de configuration complet (interfaces et points de terminaison compris).</summary>
    public byte[] ConfigurationDescriptor { get; }

    /// <summary>Le descripteur de configuration décodé.</summary>
    public UsbConfiguration Configuration { get; }

    /// <summary>La vitesse USB de l'adaptateur.</summary>
    public UsbSpeed Speed { get; }

    /// <summary>
    /// Erreurs Win32 qui veulent dire que l'adaptateur est parti (débranché, réénuméré, repris par un autre pilote) :
    /// 2 fichier introuvable, 6 poignée invalide, 22 commande inconnue, 433 périphérique inexistant, 1167 non connecté.
    /// </summary>
    public static bool IsDeviceLost(int error) => error is 2 or 6 or 22 or 433 or 1167;

    /// <summary>
    /// Ouvre l'adaptateur par son interface « périphérique USB ». Lance une <see cref="Win32Exception"/> en cas
    /// d'échec ; son code dit pourquoi (433 = la pile de l'ancien pilote n'est pas encore partie).
    /// </summary>
    public static WinUsbDevice Open(string instanceId)
    {
        string path = DeviceNodes.UsbDeviceInterfacePath(instanceId);
        SafeFileHandle file = PInvoke.CreateFile(path, 0xC0000000 /* GENERIC_READ | GENERIC_WRITE */,
            FILE_SHARE_MODE.FILE_SHARE_READ | FILE_SHARE_MODE.FILE_SHARE_WRITE, null, FILE_CREATION_DISPOSITION.OPEN_EXISTING,
            FILE_FLAGS_AND_ATTRIBUTES.FILE_FLAG_OVERLAPPED, null);
        if (file.IsInvalid) throw new Win32Exception(Marshal.GetLastPInvokeError(), $"Ouverture de {path} impossible");
        var interfaces = new List<WinUsb_FreeSafeHandle>();
        try
        {
            if (!PInvoke.WinUsb_Initialize(file, out WinUsb_FreeSafeHandle first))
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "WinUsb_Initialize a échoué");
            interfaces.Add(first);
            byte[] device = Descriptor(first, 0x01, 18);
            byte[] head = Descriptor(first, 0x02, 9);
            byte[] configuration = Descriptor(first, 0x02, BitConverter.ToUInt16(head, 2));
            for (byte i = 0; i < head[4] - 1; i++) // bNumInterfaces - 1 interfaces associées
            {
                if (!PInvoke.WinUsb_GetAssociatedInterface(first, i, out WinUsb_FreeSafeHandle more))
                    throw new Win32Exception(Marshal.GetLastPInvokeError(), $"Interface associée {i} inaccessible");
                interfaces.Add(more);
            }
            // Jamais de mise en veille sélective : les liaisons Bluetooth restent ouvertes même quand l'USB se tait
            // (Linux refuse aussi l'autosuspend avec des connexions), et certaines puces y perdent leur firmware.
            PInvoke.WinUsb_SetPowerPolicy(first, WINUSB_POWER_POLICY.AUTO_SUSPEND, [0]);
            return new WinUsbDevice(file, interfaces, device, configuration, QuerySpeed(first));
        }
        catch
        {
            foreach (WinUsb_FreeSafeHandle handle in interfaces) handle.Dispose();
            file.Dispose();
            throw;
        }
    }

    /// <summary>Transfert de contrôle, par la poignée de l'interface visée (WinUSB y met lui-même son numéro).</summary>
    public int Control(ControlRequest request, Span<byte> buffer, out uint got)
    {
        int index = request.Recipient switch
        {
            1 => InterfaceIndex(request.InterfaceNumber),
            2 => PipeInterface((byte)request.Index),
            _ => 0,
        };
        if (index < 0) index = 0;
        var setup = new WINUSB_SETUP_PACKET
        {
            RequestType = request.RequestType,
            Request = request.Request,
            Value = request.Value,
            Index = request.Index,
            Length = (ushort)buffer.Length,
        };
        return PInvoke.WinUsb_ControlTransfer(_interfaces[index], setup, buffer, out got, null) ? 0 : Marshal.GetLastPInvokeError();
    }

    /// <summary>Lecture sur un point de terminaison IN (interruption ou bulk). Attend que des données arrivent.</summary>
    public int Read(byte pipe, Span<byte> buffer, out uint got)
    {
        got = 0;
        int index = PipeInterface(pipe);
        if (index < 0) return 87; // ERROR_INVALID_PARAMETER : ce tuyau n'existe pas dans les réglages actuels
        return PInvoke.WinUsb_ReadPipe(_interfaces[index], pipe, buffer, out got, null) ? 0 : Marshal.GetLastPInvokeError();
    }

    /// <summary>Écriture sur un point de terminaison OUT.</summary>
    public int Write(byte pipe, ReadOnlySpan<byte> data, out uint got)
    {
        got = 0;
        int index = PipeInterface(pipe);
        if (index < 0) return 87;
        return PInvoke.WinUsb_WritePipe(_interfaces[index], pipe, data, out got, null) ? 0 : Marshal.GetLastPInvokeError();
    }

    /// <summary>Interrompt le transfert en cours sur un tuyau (il se termine avec l'erreur 995).</summary>
    public void Abort(byte pipe)
    {
        int index = PipeInterface(pipe);
        if (index >= 0) PInvoke.WinUsb_AbortPipe(_interfaces[index], pipe);
    }

    /// <summary>Débloque un tuyau (CLEAR_FEATURE ENDPOINT_HALT).</summary>
    public void ResetPipe(byte pipe)
    {
        int index = PipeInterface(pipe);
        if (index >= 0) PInvoke.WinUsb_ResetPipe(_interfaces[index], pipe);
    }

    /// <summary>Change le réglage alternatif d'une interface (SET_INTERFACE). Rend une erreur Win32.</summary>
    public int SetAlternate(byte interfaceNumber, byte alternate)
    {
        int index = InterfaceIndex(interfaceNumber);
        if (index < 0) return 87;
        if (!PInvoke.WinUsb_SetCurrentAlternateSetting(_interfaces[index], alternate)) return Marshal.GetLastPInvokeError();
        lock (_mapLock)
        {
            _alternates[index] = alternate;
            MapPipes();
        }
        return 0;
    }

    /// <summary>Le fichier ouvert de l'adaptateur (pour annuler un transfert isochrone précis).</summary>
    public SafeHandle File => _file;

    /// <summary>
    /// Le tuyau isochrone <paramref name="pipe"/> dans les réglages actuels : la poignée de son interface, son numéro
    /// d'interface et sa taille de paquet par trame. Null s'il n'existe pas ou n'a pas de bande passante (réglage 0).
    /// </summary>
    public (SafeHandle Interface, byte InterfaceNumber, int BytesPerInterval)? IsoPipe(byte pipe)
    {
        lock (_mapLock)
        {
            int index = PipeInterface(pipe);
            if (index < 0) return null;
            var (isoIn, isoOut, maxPacket) = Configuration.IsoPipes(_interfaceNumbers[index], _alternates[index]);
            if (maxPacket == 0 || (pipe != isoIn && pipe != isoOut)) return null;
            return (_interfaces[index], _interfaceNumbers[index], maxPacket);
        }
    }

    /// <summary>Remet le réglage 0 sur toutes les interfaces sauf la première : rend la bande passante d'un appel.</summary>
    public void ResetAlternates()
    {
        foreach (byte number in _interfaceNumbers.Skip(1)) SetAlternate(number, 0);
    }

    public void Dispose()
    {
        for (int i = _interfaces.Count - 1; i >= 0; i--) _interfaces[i].Dispose();
        _file.Dispose();
    }

    private int InterfaceIndex(byte number) => Array.IndexOf(_interfaceNumbers, number);

    private int PipeInterface(byte pipe) => Volatile.Read(ref _pipes).TryGetValue(pipe, out int index) ? index : -1;

    private void MapPipes()
    {
        var pipes = new Dictionary<byte, int>();
        for (int i = 0; i < _interfaceNumbers.Length; i++)
        {
            UsbInterfaceSetting? setting = Configuration.Settings.FirstOrDefault(
                s => s.Number == _interfaceNumbers[i] && s.Alternate == _alternates[i]);
            foreach (UsbEndpoint e in setting?.Endpoints ?? []) pipes[e.Address] = i;
        }
        Volatile.Write(ref _pipes, pipes);
    }

    private static byte[] Descriptor(SafeHandle usb, byte type, int length)
    {
        var buffer = new byte[length];
        if (!PInvoke.WinUsb_GetDescriptor(usb, type, 0, 0, buffer, out uint got))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), $"Lecture du descripteur {type} impossible");
        return buffer[..(int)got];
    }

    // DEVICE_SPEED (information n° 1) : voir UsbIpFrames.SpeedFromWinUsb ; pleine vitesse si illisible.
    private static UsbSpeed QuerySpeed(SafeHandle usb)
    {
        Span<byte> value = stackalloc byte[1];
        uint length = 1;
        return PInvoke.WinUsb_QueryDeviceInformation(usb, 1, ref length, value)
            ? UsbIpFrames.SpeedFromWinUsb(value[0])
            : UsbSpeed.Full;
    }
}
