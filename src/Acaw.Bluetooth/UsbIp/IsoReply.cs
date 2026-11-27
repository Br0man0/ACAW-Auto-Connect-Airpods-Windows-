namespace Acaw.Bluetooth.UsbIp;

/// <summary>
/// Résultat d'un paquet isochrone exécuté sur le vrai adaptateur.
/// </summary>
/// <param name="Offset">Pour un transfert entrant : où le contrôleur a placé les données du paquet dans le tampon.</param>
/// <param name="Actual">Nombre d'octets réellement transférés.</param>
/// <param name="Ok">true si le paquet s'est bien passé.</param>
public readonly record struct IsoPacketResult(int Offset, int Actual, bool Ok);

/// <summary>
/// Fabrique la réponse USB/IP d'un transfert isochrone (le son des appels Bluetooth, en paquets SCO).
/// </summary>
/// <remarks>
/// Format attendu par usbip-win2 : les données entrantes sont mises <b>bout à bout</b> après l'en-tête, puis
/// viennent les descripteurs des paquets, qui gardent l'offset et la longueur de la demande.
/// <para>
/// Piège connu (relevé par WPods) : usbip-win2 reçoit les données tassées au début du tampon, puis déplace
/// chaque paquet vers son offset. Si un paquet avec données est déclaré plus loin qu'en tête, il laisse une
/// copie au début, que le pilote Bluetooth de Windows lit comme un paquet SCO de plus : chaque trame d'appel
/// arrive deux fois (voix « au ralenti »). Les paquets porteurs de données (ou en erreur) sont donc déclarés
/// dans les <b>premiers</b> descripteurs, dans l'ordre ; les suivants restent vides et sans erreur.
/// </para>
/// </remarks>
public static class IsoReply
{
    /// <summary>
    /// La réponse complète (en-tête, données, descripteurs).
    /// </summary>
    /// <param name="seq">Numéro de la demande.</param>
    /// <param name="status">État global du transfert (0 = réussi).</param>
    /// <param name="startFrame">Trame de départ à renvoyer.</param>
    /// <param name="requested">Les paquets de la demande.</param>
    /// <param name="isIn">true pour un transfert entrant.</param>
    /// <param name="inData">Tampon rempli par le contrôleur (transfert entrant), vide sinon.</param>
    /// <param name="results">Résultat de chaque paquet, dans l'ordre ; null si le transfert entier a échoué.</param>
    public static byte[] Build(uint seq, int status, uint startFrame, IReadOnlyList<IsoPacket> requested, bool isIn,
                               ReadOnlySpan<byte> inData, IReadOnlyList<IsoPacketResult>? results)
    {
        int count = requested.Count;
        if (results is not null && results.Count != count)
            throw new ArgumentException("Un résultat par paquet demandé.", nameof(results));

        var descriptors = new (uint Actual, int Status)[count];
        var payload = new List<(int Offset, int Length)>();
        int total = 0;
        int next = 0; // transfert entrant : prochain résultat à déclarer

        for (int i = 0; i < count; i++)
        {
            int length = (int)Math.Min(requested[i].Length, int.MaxValue);
            if (results is null)
            {
                descriptors[i] = (0, UsbIpFrames.EXDEV);
            }
            else if (isIn)
            {
                // Saute les paquets vides et réussis : ils ne prennent pas de place en tête.
                while (next < count && results[next].Actual <= 0 && results[next].Ok) next++;
                if (next < count)
                {
                    IsoPacketResult r = results[next++];
                    int actual = Math.Clamp(r.Actual, 0, length);
                    if (actual > 0) payload.Add((r.Offset, actual));
                    descriptors[i] = ((uint)actual, r.Ok ? 0 : UsbIpFrames.EXDEV);
                }
                else
                {
                    descriptors[i] = (0, 0);
                }
            }
            else
            {
                IsoPacketResult r = results[i];
                descriptors[i] = ((uint)Math.Clamp(r.Actual, 0, length), r.Ok ? 0 : UsbIpFrames.EXDEV);
            }
            total += (int)descriptors[i].Actual;
        }

        int dataLength = payload.Sum(p => p.Length);
        var frame = new byte[UsbIpFrames.HeaderSize + dataLength + count * UsbIpFrames.IsoDescriptorSize];
        UsbIpFrames.WriteU32(frame, 0, (uint)UsbIpCommand.RetSubmit);
        UsbIpFrames.WriteU32(frame, 4, seq);
        UsbIpFrames.WriteI32(frame, 20, status);
        UsbIpFrames.WriteI32(frame, 24, total);
        UsbIpFrames.WriteU32(frame, 28, startFrame);
        UsbIpFrames.WriteI32(frame, 32, count);
        UsbIpFrames.WriteI32(frame, 36, descriptors.Count(d => d.Status != 0)); // error_count

        int at = UsbIpFrames.HeaderSize;
        foreach (var (offset, length) in payload)
        {
            if (offset < 0 || offset + length > inData.Length)
                throw new ArgumentException("Un paquet sort du tampon reçu.", nameof(results));
            inData.Slice(offset, length).CopyTo(frame.AsSpan(at));
            at += length;
        }
        for (int i = 0; i < count; i++, at += UsbIpFrames.IsoDescriptorSize)
        {
            UsbIpFrames.WriteU32(frame, at, requested[i].Offset);
            UsbIpFrames.WriteU32(frame, at + 4, requested[i].Length);
            UsbIpFrames.WriteU32(frame, at + 8, descriptors[i].Actual);
            UsbIpFrames.WriteI32(frame, at + 12, descriptors[i].Status);
        }
        return frame;
    }
}
