using System.Buffers.Binary;
using System.Text;

namespace Acaw.Bluetooth.UsbIp;

// Le protocole USB/IP fait passer un périphérique USB sur une connexion TCP. Ici, la connexion est locale
// (127.0.0.1) : usbip-win2 joue l'ordinateur « client » qui voit un adaptateur virtuel, et le service d'ACAW
// joue le « serveur » qui exécute chaque transfert sur le vrai adaptateur.
// Référence : la documentation du noyau Linux, « USB/IP protocol » (usbip_protocol.rst).
// Tous les entiers sont en big-endian (octet de poids fort en premier), à l'inverse de l'USB lui-même.

/// <summary>Les quatre commandes d'une connexion USB/IP établie.</summary>
public enum UsbIpCommand : uint
{
    /// <summary>Demande d'un transfert (client → serveur).</summary>
    Submit = 1,

    /// <summary>Annulation d'un transfert en cours (client → serveur).</summary>
    Unlink = 2,

    /// <summary>Résultat d'un transfert (serveur → client).</summary>
    RetSubmit = 3,

    /// <summary>Résultat d'une annulation (serveur → client).</summary>
    RetUnlink = 4,
}

/// <summary>Vitesse du périphérique, telle que USB/IP la déclare.</summary>
public enum UsbSpeed : uint
{
    Low = 1,
    Full = 2,
    High = 3,
}

/// <summary>Un paquet d'un transfert isochrone : sa place et sa taille dans le tampon du transfert.</summary>
public readonly record struct IsoPacket(uint Offset, uint Length);

/// <summary>
/// L'en-tête de 48 octets d'une commande reçue, avec la taille de ce qui le suit sur la connexion.
/// </summary>
/// <param name="Command">La commande.</param>
/// <param name="SeqNum">Numéro de la demande ; la réponse le reprend.</param>
/// <param name="DevId">Identifiant du périphérique côté client (repris tel quel).</param>
/// <param name="In">true pour un transfert du périphérique vers l'ordinateur.</param>
/// <param name="Endpoint">Numéro du point de terminaison (0 = contrôle), sans le bit de direction.</param>
/// <param name="TransferFlags">Options du transfert (non utilisées par le relais, reprises pour le journal).</param>
/// <param name="BufferLength">Taille du tampon du transfert.</param>
/// <param name="StartFrame">Trame de départ (isochrone).</param>
/// <param name="PacketCount">Nombre de paquets isochrones ; 0 pour un transfert ordinaire.</param>
/// <param name="Interval">Intervalle demandé (isochrone, interruption).</param>
/// <param name="Setup">Les 8 octets du paquet de configuration (transferts de contrôle).</param>
/// <param name="UnlinkSeqNum">Pour <see cref="UsbIpCommand.Unlink"/> : le numéro de la demande à annuler.</param>
public sealed record UsbIpRequestHeader(
    UsbIpCommand Command, uint SeqNum, uint DevId, bool In, uint Endpoint, uint TransferFlags,
    int BufferLength, int StartFrame, int PacketCount, int Interval, byte[] Setup, uint UnlinkSeqNum)
{
    /// <summary>
    /// Octets qui suivent l'en-tête : les données sortantes (OUT), puis 16 octets par paquet isochrone.
    /// Une annulation n'est suivie de rien.
    /// </summary>
    public int PayloadLength =>
        Command != UsbIpCommand.Submit ? 0 : (In ? 0 : BufferLength) + PacketCount * UsbIpFrames.IsoDescriptorSize;

    /// <summary>L'adresse USB complète du point de terminaison : bit 7 = IN, comme dans les descripteurs.</summary>
    public byte EndpointAddress => (byte)(Endpoint | (In ? 0x80u : 0u));
}

/// <summary>Un transfert demandé, avec ses données sortantes et ses paquets isochrones.</summary>
public sealed record UsbIpSubmit(UsbIpRequestHeader Header, byte[] OutData, IsoPacket[] Packets)
{
    public bool IsIsochronous => Packets.Length > 0;
}

/// <summary>Lecture et écriture des trames USB/IP.</summary>
public static class UsbIpFrames
{
    /// <summary>Taille de l'en-tête de chaque commande et de chaque réponse.</summary>
    public const int HeaderSize = 48;

    /// <summary>Taille du descripteur d'un paquet isochrone (offset, longueur, longueur réelle, état).</summary>
    public const int IsoDescriptorSize = 16;

    /// <summary>Taille d'une demande d'import (8 octets d'opération + 32 octets d'identifiant de bus).</summary>
    public const int ImportRequestSize = 40;

    /// <summary>Taille de la description du périphérique envoyée avec un import accepté.</summary>
    public const int DeviceInfoSize = 312;

    /// <summary>Version du protocole d'import (1.1.1).</summary>
    public const ushort ProtocolVersion = 0x0111;

    /// <summary>Code d'une demande d'import (OP_REQ_IMPORT).</summary>
    public const ushort OpRequestImport = 0x8003;

    /// <summary>Code d'une réponse d'import (OP_REP_IMPORT).</summary>
    public const ushort OpReplyImport = 0x0003;

    /// <summary>Valeur de « nombre de paquets » d'un transfert qui n'est pas isochrone, attendue par usbip-win2.</summary>
    public const uint NotIsochronous = 0xFFFFFFFF;

    // Codes d'erreur Linux (errno, négatifs) que le client attend dans les réponses.
    public const int ECONNRESET = -104;
    public const int EPIPE = -32;
    public const int EPROTO = -71;
    public const int EXDEV = -18;
    public const int ETIMEDOUT = -110;

    /// <summary>Décode l'en-tête de 48 octets d'une commande reçue.</summary>
    /// <exception cref="FormatException">Commande inconnue, ou tailles impossibles.</exception>
    public static UsbIpRequestHeader ParseRequestHeader(ReadOnlySpan<byte> header)
    {
        if (header.Length < HeaderSize) throw new FormatException("En-tête USB/IP trop court.");
        var command = (UsbIpCommand)U32(header, 0);
        uint seq = U32(header, 4);
        uint devId = U32(header, 8);
        bool isIn = U32(header, 12) != 0;
        uint endpoint = U32(header, 16);
        switch (command)
        {
            case UsbIpCommand.Submit:
                int length = I32(header, 24);
                int packets = I32(header, 32);
                // « Pas isochrone » s'écrit 0 ou 0xFFFFFFFF selon les clients.
                if (packets == -1) packets = 0;
                if (length < 0 || packets < 0 || packets > 1024)
                    throw new FormatException($"Transfert USB/IP impossible : {length} octets, {packets} paquets.");
                return new UsbIpRequestHeader(command, seq, devId, isIn, endpoint, U32(header, 20), length,
                                              I32(header, 28), packets, I32(header, 36), header[40..48].ToArray(), 0);
            case UsbIpCommand.Unlink:
                return new UsbIpRequestHeader(command, seq, devId, isIn, endpoint, 0, 0, 0, 0, 0, new byte[8],
                                              U32(header, 20));
            default:
                throw new FormatException($"Commande USB/IP inattendue : {(uint)command}.");
        }
    }

    /// <summary>Complète une demande de transfert avec ce qui suit son en-tête (<see cref="UsbIpRequestHeader.PayloadLength"/> octets).</summary>
    public static UsbIpSubmit ParseSubmit(UsbIpRequestHeader header, ReadOnlySpan<byte> payload)
    {
        if (header.Command != UsbIpCommand.Submit) throw new ArgumentException("Ce n'est pas une demande de transfert.", nameof(header));
        if (payload.Length != header.PayloadLength)
            throw new FormatException($"Données USB/IP : {payload.Length} octets au lieu de {header.PayloadLength}.");
        int dataLength = header.In ? 0 : header.BufferLength;
        var packets = new IsoPacket[header.PacketCount];
        for (int i = 0; i < packets.Length; i++)
        {
            int at = dataLength + i * IsoDescriptorSize;
            packets[i] = new IsoPacket(U32(payload, at), U32(payload, at + 4));
        }
        return new UsbIpSubmit(header, payload[..dataLength].ToArray(), packets);
    }

    /// <summary>Réponse à un transfert ordinaire (non isochrone) : état, longueur réelle, données entrantes.</summary>
    public static byte[] RetSubmit(uint seq, int status, int actualLength, ReadOnlySpan<byte> inData)
    {
        var frame = new byte[HeaderSize + inData.Length];
        WriteU32(frame, 0, (uint)UsbIpCommand.RetSubmit);
        WriteU32(frame, 4, seq);
        WriteI32(frame, 20, status);
        WriteI32(frame, 24, actualLength);
        WriteU32(frame, 32, NotIsochronous);
        inData.CopyTo(frame.AsSpan(HeaderSize));
        return frame;
    }

    /// <summary>
    /// Réponse à une annulation : <see cref="ECONNRESET"/> si le transfert a bien été annulé, 0 s'il était
    /// déjà terminé (sa réponse est alors partie avant).
    /// </summary>
    public static byte[] RetUnlink(uint seq, int status)
    {
        var frame = new byte[HeaderSize];
        WriteU32(frame, 0, (uint)UsbIpCommand.RetUnlink);
        WriteU32(frame, 4, seq);
        WriteI32(frame, 20, status);
        return frame;
    }

    /// <summary>
    /// Lit une demande d'import (les 40 premiers octets envoyés par le client sur une nouvelle connexion).
    /// Rend l'identifiant de bus demandé, ou null si ce n'est pas une demande d'import.
    /// </summary>
    public static string? ParseImportRequest(ReadOnlySpan<byte> request)
    {
        if (request.Length < ImportRequestSize || BinaryPrimitives.ReadUInt16BigEndian(request[2..]) != OpRequestImport)
            return null;
        ReadOnlySpan<byte> bus = request.Slice(8, 32);
        int end = bus.IndexOf((byte)0);
        return Encoding.ASCII.GetString(end < 0 ? bus : bus[..end]);
    }

    /// <summary>Réponse d'import : acceptée (suivie de la description du périphérique) ou refusée.</summary>
    public static byte[] ImportReply(byte[]? deviceInfo)
    {
        var frame = new byte[8 + (deviceInfo?.Length ?? 0)];
        BinaryPrimitives.WriteUInt16BigEndian(frame, ProtocolVersion);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(2), OpReplyImport);
        WriteU32(frame, 4, deviceInfo is null ? 1u : 0u);
        deviceInfo?.CopyTo(frame, 8);
        return frame;
    }

    /// <summary>
    /// Description du périphérique (structure usbip_usb_device, 312 octets) tirée de ses descripteurs USB
    /// (qui, eux, sont en little-endian).
    /// </summary>
    public static byte[] DeviceInfo(string busId, ReadOnlySpan<byte> deviceDescriptor, ReadOnlySpan<byte> configDescriptor, UsbSpeed speed)
    {
        if (deviceDescriptor.Length < 18 || configDescriptor.Length < 9)
            throw new ArgumentException("Descripteurs USB incomplets.");
        if (busId.Length is 0 or > 31) throw new ArgumentException("Identifiant de bus invalide.", nameof(busId));
        var info = new byte[DeviceInfoSize];
        Encoding.ASCII.GetBytes("/sys/devices/platform/acaw/" + busId).CopyTo(info, 0);   // chemin, 256 octets
        Encoding.ASCII.GetBytes(busId).CopyTo(info, 256);                                 // bus, 32 octets
        WriteU32(info, 288, 1);                                                           // numéro de bus
        WriteU32(info, 292, 1);                                                           // numéro de périphérique
        WriteU32(info, 296, (uint)speed);
        BinaryPrimitives.WriteUInt16BigEndian(info.AsSpan(300), BinaryPrimitives.ReadUInt16LittleEndian(deviceDescriptor[8..]));  // idVendor
        BinaryPrimitives.WriteUInt16BigEndian(info.AsSpan(302), BinaryPrimitives.ReadUInt16LittleEndian(deviceDescriptor[10..])); // idProduct
        BinaryPrimitives.WriteUInt16BigEndian(info.AsSpan(304), BinaryPrimitives.ReadUInt16LittleEndian(deviceDescriptor[12..])); // bcdDevice
        info[306] = deviceDescriptor[4];  // bDeviceClass
        info[307] = deviceDescriptor[5];  // bDeviceSubClass
        info[308] = deviceDescriptor[6];  // bDeviceProtocol
        info[309] = configDescriptor[5];  // bConfigurationValue
        info[310] = deviceDescriptor[17]; // bNumConfigurations
        info[311] = configDescriptor[4];  // bNumInterfaces
        return info;
    }

    /// <summary>
    /// La vitesse à déclarer à usbip-win2, d'après la valeur DEVICE_SPEED de WinUSB.
    /// </summary>
    /// <remarks>
    /// WinUSB ne distingue que deux cas : 1 (« LowSpeed ») pour un périphérique basse <b>ou pleine</b> vitesse,
    /// 3 (« HighSpeed ») pour la haute vitesse. Vu sur l'AX201 de Romsy (test R1, 2026-11-27) : 1, alors qu'il est
    /// en pleine vitesse. Un adaptateur Bluetooth n'est jamais en basse vitesse (elle interdit les transferts bulk
    /// et isochrones), donc 1 veut dire pleine vitesse.
    /// </remarks>
    public static UsbSpeed SpeedFromWinUsb(byte deviceSpeed) => deviceSpeed == 3 ? UsbSpeed.High : UsbSpeed.Full;

    /// <summary>
    /// Traduit l'erreur Windows d'un transfert WinUSB en code d'erreur Linux pour la réponse :
    /// annulé → ECONNRESET, bloqué (stall) → EPIPE, délai dépassé → ETIMEDOUT, le reste → EPROTO.
    /// </summary>
    public static int ErrnoFromWin32(int win32Error) => win32Error switch
    {
        0 => 0,
        995 => ECONNRESET,   // ERROR_OPERATION_ABORTED
        31 => EPIPE,         // ERROR_GEN_FAILURE : le périphérique a refusé (stall)
        121 => ETIMEDOUT,    // ERROR_SEM_TIMEOUT
        _ => EPROTO,
    };

    internal static uint U32(ReadOnlySpan<byte> b, int at) => BinaryPrimitives.ReadUInt32BigEndian(b[at..]);
    internal static int I32(ReadOnlySpan<byte> b, int at) => BinaryPrimitives.ReadInt32BigEndian(b[at..]);
    internal static void WriteU32(Span<byte> b, int at, uint value) => BinaryPrimitives.WriteUInt32BigEndian(b[at..], value);
    internal static void WriteI32(Span<byte> b, int at, int value) => BinaryPrimitives.WriteInt32BigEndian(b[at..], value);
}
