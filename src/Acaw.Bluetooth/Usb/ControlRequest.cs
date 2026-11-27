using System.Buffers.Binary;

namespace Acaw.Bluetooth.Usb;

/// <summary>Ce que le relais doit faire d'un transfert de contrôle.</summary>
public enum ControlKind
{
    /// <summary>À envoyer tel quel à l'adaptateur.</summary>
    Forward,

    /// <summary>Commande HCI (contrôle de classe sortant) : envoyée telle quelle, mais comptée.</summary>
    HciCommand,

    /// <summary>GET_DESCRIPTOR(périphérique) : on répond avec le descripteur retouché.</summary>
    DeviceDescriptor,

    /// <summary>GET_DESCRIPTOR(configuration) : on répond avec le descripteur retouché.</summary>
    ConfigurationDescriptor,

    /// <summary>GET_DESCRIPTOR(chaîne n° 0) : la liste des langues.</summary>
    LanguagesDescriptor,

    /// <summary>GET_DESCRIPTOR(chaîne) à l'index du numéro de série virtuel.</summary>
    SerialDescriptor,

    /// <summary>SET_CONFIGURATION : WinUSB a déjà configuré l'adaptateur, on répond « fait ».</summary>
    SetConfiguration,

    /// <summary>SET_INTERFACE : changer le réglage alternatif d'une interface.</summary>
    SetInterface,

    /// <summary>CLEAR_FEATURE(ENDPOINT_HALT) : débloquer un point de terminaison.</summary>
    ClearHalt,
}

/// <summary>
/// Un paquet de configuration USB (les 8 octets qui ouvrent chaque transfert de contrôle) et ce qu'il demande.
/// </summary>
/// <remarks>
/// WinUSB garde pour lui quelques requêtes standard (configuration, réglage d'interface) et refuse qu'un
/// programme les envoie : le relais les imite. Il répond aussi lui-même aux descripteurs, pour montrer à Windows
/// les versions retouchées (<see cref="DescriptorPatch"/>).
/// </remarks>
public readonly record struct ControlRequest(byte RequestType, byte Request, ushort Value, ushort Index, ushort Length)
{
    /// <summary>Lit les 8 octets du paquet de configuration.</summary>
    public static ControlRequest Parse(ReadOnlySpan<byte> setup)
    {
        if (setup.Length < 8) throw new ArgumentException("Un paquet de configuration fait 8 octets.", nameof(setup));
        return new ControlRequest(setup[0], setup[1], BinaryPrimitives.ReadUInt16LittleEndian(setup[2..]),
                                  BinaryPrimitives.ReadUInt16LittleEndian(setup[4..]), BinaryPrimitives.ReadUInt16LittleEndian(setup[6..]));
    }

    /// <summary>true pour une requête du périphérique vers l'ordinateur (bit 7 de bmRequestType).</summary>
    public bool In => (RequestType & 0x80) != 0;

    /// <summary>Destinataire : 0 périphérique, 1 interface, 2 point de terminaison.</summary>
    public int Recipient => RequestType & 0x1F;

    /// <summary>Le numéro d'interface visé (requête adressée à une interface).</summary>
    public byte InterfaceNumber => (byte)(Index & 0xFF);

    /// <summary>Les 8 octets, en entier little-endian, comme WinUSB les attend (WINUSB_SETUP_PACKET).</summary>
    public ulong Packed =>
        RequestType | (ulong)Request << 8 | (ulong)Value << 16 | (ulong)Index << 32 | (ulong)Length << 48;

    /// <summary>Ce que le relais doit en faire.</summary>
    /// <param name="serialIndex">Index de chaîne choisi pour le numéro de série virtuel.</param>
    public ControlKind Classify(byte serialIndex)
    {
        byte descriptorType = (byte)(Value >> 8);
        byte descriptorIndex = (byte)Value;
        return (RequestType, Request) switch
        {
            (0x20, _) => ControlKind.HciCommand,
            (0x80, 0x06) when descriptorType == 0x01 => ControlKind.DeviceDescriptor,
            (0x80, 0x06) when descriptorType == 0x02 => ControlKind.ConfigurationDescriptor,
            (0x80, 0x06) when descriptorType == 0x03 && descriptorIndex == 0 => ControlKind.LanguagesDescriptor,
            (0x80, 0x06) when descriptorType == 0x03 && descriptorIndex == serialIndex => ControlKind.SerialDescriptor,
            (0x00, 0x09) => ControlKind.SetConfiguration,
            (0x01, 0x0B) => ControlKind.SetInterface,
            (0x02, 0x01) when Value == 0 => ControlKind.ClearHalt,
            _ => ControlKind.Forward,
        };
    }
}
