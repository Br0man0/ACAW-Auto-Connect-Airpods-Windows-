namespace Acaw.Core.Ble;

/// <summary>Décode les données fabricant Apple d'une annonce BLE.</summary>
public static class AppleAdvertisementParser
{
    /// <summary>Identifiant fabricant d'Apple dans les annonces BLE.</summary>
    public const ushort AppleCompanyId = 0x004C;

    private const byte ProximityPairingType = 0x07;
    private const int MinimumLength = 9; // partie en clair : 01 + modèle(2) + 6 octets

    /// <summary>
    /// Les données Apple sont une suite de blocs [type][longueur][valeur] ; on cherche le bloc 0x07.
    /// Renvoie null si la trame n'en contient pas ou si elle est tronquée.
    /// </summary>
    public static ProximityFrame? TryParse(ReadOnlySpan<byte> appleData)
    {
        int i = 0;
        while (i + 2 <= appleData.Length)
        {
            byte type = appleData[i];
            int length = appleData[i + 1];
            if (i + 2 + length > appleData.Length) return null;
            if (type == ProximityPairingType && length >= MinimumLength)
            {
                var v = appleData.Slice(i + 2, length);
                // Le modèle est écrit octet de poids faible d'abord : 27 20 → 0x2027.
                return new ProximityFrame($"{v[2]:X2}{v[1]:X2}", v[3], v[4], v[5], v[6], v[7]);
            }
            i += 2 + length;
        }
        return null;
    }
}
