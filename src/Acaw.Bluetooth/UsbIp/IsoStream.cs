namespace Acaw.Bluetooth.UsbIp;

/// <summary>
/// Quand demander à WinUSB d'enchaîner un transfert isochrone juste après le précédent (« ContinueStream »).
/// </summary>
/// <remarks>
/// <para>Avec ContinueStream, WinUSB place le transfert dans la trame qui suit immédiatement le dernier transfert
/// encore en cours ; si aucun n'est en cours (le flux s'est tari), c'est impossible et le transfert <b>échoue</b>
/// (erreur 87, « paramètre invalide »), parfois seulement à la fin, donc trop tard pour réessayer.</para>
/// <list type="bullet">
/// <item><b>Réception (IN)</b> : Windows garde plusieurs lectures en attente, le flux est continu ; on enchaîne
/// tant qu'un transfert est en vol, sinon on repart « dès que possible ».</item>
/// <item><b>Envoi (OUT)</b> : Windows envoie la voix par petits paquets espacés (un paquet toutes les quelques
/// trames) ; le précédent est presque toujours fini. On n'enchaîne <b>jamais</b> : chaque envoi part à la
/// prochaine trame possible.</item>
/// </list>
/// Vu au test R4 bis (2026-11-28) : en enchaînant aussi les envois, 26 à 50 % d'entre eux échouaient avec
/// l'erreur 87, et le son devenait robotique. WPods n'enchaîne pas les envois non plus.
/// </remarks>
public static class IsoStream
{
    /// <summary>true s'il faut demander l'enchaînement au transfert suivant.</summary>
    /// <param name="isIn">true pour une réception.</param>
    /// <param name="inFlight">Nombre de transferts encore en cours sur ce tuyau.</param>
    public static bool ShouldContinue(bool isIn, int inFlight) => isIn && inFlight > 0;
}
