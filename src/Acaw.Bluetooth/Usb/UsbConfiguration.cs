using System.Buffers.Binary;

namespace Acaw.Bluetooth.Usb;

/// <summary>Type de transfert d'un point de terminaison USB (2 bits bas de bmAttributes).</summary>
public enum UsbTransferType : byte
{
    Control = 0,
    Isochronous = 1,
    Bulk = 2,
    Interrupt = 3,
}

/// <summary>Un point de terminaison (« endpoint ») : une file de transfert du périphérique.</summary>
/// <param name="Address">Adresse : bit 7 = IN (du périphérique vers l'ordinateur), bits 0-3 = numéro.</param>
public readonly record struct UsbEndpoint(byte Address, UsbTransferType Type, ushort MaxPacketSize, byte Interval)
{
    public bool In => (Address & 0x80) != 0;
}

/// <summary>Un réglage d'une interface (une interface peut en avoir plusieurs : « alternate settings »).</summary>
public sealed record UsbInterfaceSetting(byte Number, byte Alternate, byte Class, byte SubClass, byte Protocol,
                                         IReadOnlyList<UsbEndpoint> Endpoints);

/// <summary>
/// Lecture du descripteur de configuration d'un périphérique USB : la liste de ses interfaces et de leurs
/// points de terminaison.
/// </summary>
/// <remarks>
/// Un adaptateur Bluetooth USB a d'habitude deux interfaces (spécification Bluetooth, partie « USB transport ») :
/// l'interface HCI (un IN d'interruption pour les événements, un IN et un OUT bulk pour les données ACL), puis
/// l'interface audio des appels, dont chaque réglage alternatif réserve plus de bande passante isochrone (SCO).
/// </remarks>
public sealed class UsbConfiguration
{
    private UsbConfiguration(IReadOnlyList<UsbInterfaceSetting> settings) => Settings = settings;

    /// <summary>Tous les réglages de toutes les interfaces, dans l'ordre du descripteur.</summary>
    public IReadOnlyList<UsbInterfaceSetting> Settings { get; }

    /// <summary>Décode un descripteur de configuration complet (avec ses descripteurs d'interface et d'endpoint).</summary>
    /// <exception cref="FormatException">Descripteur tronqué ou incohérent.</exception>
    public static UsbConfiguration Parse(ReadOnlySpan<byte> config)
    {
        if (config.Length < 9 || config[1] != 0x02) throw new FormatException("Ce n'est pas un descripteur de configuration.");
        int total = Math.Min(BinaryPrimitives.ReadUInt16LittleEndian(config[2..]), config.Length);
        var settings = new List<UsbInterfaceSetting>();
        List<UsbEndpoint>? endpoints = null;
        (byte Number, byte Alt, byte Class, byte Sub, byte Proto) current = default;

        for (int at = config[0]; at + 2 <= total;)
        {
            int length = config[at];
            if (length < 2 || at + length > total) throw new FormatException($"Descripteur USB tronqué à l'octet {at}.");
            byte type = config[at + 1];
            if (type == 0x04 && length >= 9) // interface
            {
                Flush();
                current = (config[at + 2], config[at + 3], config[at + 5], config[at + 6], config[at + 7]);
                endpoints = [];
            }
            else if (type == 0x05 && length >= 7 && endpoints is not null) // endpoint
            {
                endpoints.Add(new UsbEndpoint(config[at + 2], (UsbTransferType)(config[at + 3] & 0x03),
                                              (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(config[(at + 4)..]) & 0x07FF),
                                              config[at + 6]));
            }
            at += length;
        }
        Flush();
        return new UsbConfiguration(settings);

        void Flush()
        {
            if (endpoints is not null)
                settings.Add(new UsbInterfaceSetting(current.Number, current.Alt, current.Class, current.Sub, current.Proto, endpoints));
        }
    }

    /// <summary>
    /// Les tuyaux HCI : la première interface dont le réglage 0 a un IN d'interruption (événements), un IN bulk
    /// et un OUT bulk (données ACL). Null si aucune.
    /// </summary>
    public (byte Interface, byte Events, byte AclIn, byte AclOut)? FindHciPipes()
    {
        foreach (UsbInterfaceSetting s in Settings)
        {
            if (s.Alternate != 0) continue;
            // Un endpoint absent se lit comme l'adresse 0, qui n'est jamais celle d'un endpoint de données.
            byte events = s.Endpoints.FirstOrDefault(e => e.Type == UsbTransferType.Interrupt && e.In).Address;
            byte aclIn = s.Endpoints.FirstOrDefault(e => e.Type == UsbTransferType.Bulk && e.In).Address;
            byte aclOut = s.Endpoints.FirstOrDefault(e => e.Type == UsbTransferType.Bulk && !e.In).Address;
            if (events != 0 && aclIn != 0 && aclOut != 0) return (s.Number, events, aclIn, aclOut);
        }
        return null;
    }

    /// <summary>
    /// Les points de terminaison isochrones d'un réglage (0 si absent) et leur taille maximale de paquet.
    /// </summary>
    public (byte In, byte Out, ushort MaxPacket) IsoPipes(byte interfaceNumber, byte alternate)
    {
        UsbInterfaceSetting? s = Settings.FirstOrDefault(x => x.Number == interfaceNumber && x.Alternate == alternate);
        if (s is null) return (0, 0, 0);
        UsbEndpoint isoIn = s.Endpoints.FirstOrDefault(e => e.Type == UsbTransferType.Isochronous && e.In);
        UsbEndpoint isoOut = s.Endpoints.FirstOrDefault(e => e.Type == UsbTransferType.Isochronous && !e.In);
        return (isoIn.Address, isoOut.Address, Math.Max(isoIn.MaxPacketSize, isoOut.MaxPacketSize));
    }

    /// <summary>Tous les index de chaînes (iInterface) cités par les interfaces.</summary>
    public static IReadOnlySet<byte> StringIndexes(ReadOnlySpan<byte> config)
    {
        var indexes = new HashSet<byte>();
        if (config.Length < 9) return indexes;
        if (config[6] != 0) indexes.Add(config[6]); // iConfiguration
        int total = Math.Min(BinaryPrimitives.ReadUInt16LittleEndian(config[2..]), config.Length);
        for (int at = config[0]; at + 2 <= total && config[at] >= 2 && at + config[at] <= total; at += config[at])
        {
            if (config[at + 1] == 0x04 && config[at] >= 9 && config[at + 8] != 0) indexes.Add(config[at + 8]);
        }
        return indexes;
    }
}
