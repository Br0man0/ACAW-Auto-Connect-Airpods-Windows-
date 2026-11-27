using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Acaw.Bluetooth.Usb;

/// <summary>
/// Les retouches des descripteurs du vrai adaptateur avant de les montrer à Windows comme adaptateur virtuel.
/// Chaque retouche corrige un piège connu (relevés par WPods, voir le guide 11).
/// </summary>
public static class DescriptorPatch
{
    /// <summary>Liste des langues d'un périphérique (descripteur de chaîne 0) : anglais (États-Unis) seulement.</summary>
    public static readonly byte[] EnglishLanguages = [4, 0x03, 0x09, 0x04];

    // Index interdit pour une chaîne : Windows y lit un « descripteur OS Microsoft » à la première
    // énumération, et toute autre réponse y fait échouer l'énumération.
    private const byte MicrosoftOsIndex = 0xEE;

    /// <summary>
    /// Descripteur de périphérique retouché :
    /// <list type="bullet">
    /// <item><c>bcdUSB</c> ramené à 2.00 au plus : au-delà (certains Intel annoncent 2.50), le pilote de hub de
    /// Windows exige un descripteur BOS et des règles plus strictes, alors qu'usbip-win2 montre un périphérique
    /// haute vitesse ;</item>
    /// <item>le numéro de série remplacé par celui de l'adaptateur virtuel (index <paramref name="serialIndex"/>).</item>
    /// </list>
    /// </summary>
    public static byte[] PatchDevice(ReadOnlySpan<byte> device, byte serialIndex)
    {
        if (device.Length < 18 || device[1] != 0x01) throw new ArgumentException("Ce n'est pas un descripteur de périphérique.", nameof(device));
        byte[] patched = device[..18].ToArray();
        if (BinaryPrimitives.ReadUInt16LittleEndian(patched.AsSpan(2)) > 0x0200)
            BinaryPrimitives.WriteUInt16LittleEndian(patched.AsSpan(2), 0x0200);
        patched[16] = serialIndex; // iSerialNumber
        return patched;
    }

    /// <summary>
    /// Descripteur de configuration retouché : bit « remote wakeup » effacé. USB/IP ne transporte pas le signal
    /// de réveil ; un périphérique qui se dit capable de réveiller est mis en veille sélective par Windows et
    /// ne revient jamais.
    /// </summary>
    public static byte[] PatchConfiguration(ReadOnlySpan<byte> config)
    {
        if (config.Length < 9 || config[1] != 0x02) throw new ArgumentException("Ce n'est pas un descripteur de configuration.", nameof(config));
        byte[] patched = config.ToArray();
        patched[7] &= unchecked((byte)~0x20); // bmAttributes
        return patched;
    }

    /// <summary>
    /// Un index de chaîne libre pour le numéro de série virtuel : ni 0 (« pas de chaîne »), ni 0xEE, ni un index
    /// déjà utilisé par le périphérique (fabricant, produit, configuration, interfaces).
    /// </summary>
    public static byte ChooseSerialIndex(ReadOnlySpan<byte> device, ReadOnlySpan<byte> config)
    {
        var used = new HashSet<byte>(UsbConfiguration.StringIndexes(config));
        if (device.Length >= 18)
        {
            used.Add(device[14]); // iManufacturer
            used.Add(device[15]); // iProduct
        }
        for (int i = 0x50; i < 0xFF; i++)
        {
            if (i != MicrosoftOsIndex && !used.Contains((byte)i)) return (byte)i;
        }
        throw new InvalidOperationException("Aucun index de chaîne libre.");
    }

    /// <summary>
    /// Le numéro de série de l'adaptateur virtuel, dérivé de l'identifiant d'instance du vrai adaptateur.
    /// </summary>
    /// <remarks>
    /// Jamais le vrai numéro : beaucoup de modèles donnent le même à tous leurs exemplaires, et le vrai
    /// adaptateur reste présent avec lui. Windows nomme l'adaptateur virtuel d'après ce numéro : stable, il garde
    /// les mêmes identifiants (périphériques, endpoints audio, réglages) à chaque prise en main. Les identifiants
    /// d'instance Windows ne tiennent pas compte de la casse (« 5&amp;D58ED73 » et « 5&amp;d58ed73 » sont le
    /// même) : ils sont mis en majuscules avant le calcul.
    /// </remarks>
    public static string VirtualSerial(string realInstanceId)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(realInstanceId.ToUpperInvariant()));
        return "ACAW" + Convert.ToHexString(hash, 0, 6);
    }

    /// <summary>Un descripteur de chaîne USB (texte en UTF-16 little-endian).</summary>
    public static byte[] StringDescriptor(string text)
    {
        byte[] utf16 = Encoding.Unicode.GetBytes(text);
        if (utf16.Length > 253) throw new ArgumentException("Chaîne trop longue pour un descripteur USB.", nameof(text));
        var descriptor = new byte[2 + utf16.Length];
        descriptor[0] = (byte)descriptor.Length;
        descriptor[1] = 0x03;
        utf16.CopyTo(descriptor, 2);
        return descriptor;
    }
}
