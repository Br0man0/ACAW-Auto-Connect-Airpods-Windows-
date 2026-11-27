namespace Acaw.Bluetooth.UsbIp;

/// <summary>
/// Le rythme de l'audio sortant d'un appel (transferts isochrones OUT) : quand chaque envoi doit être déclaré
/// terminé.
/// </summary>
/// <remarks>
/// <para>Windows règle le débit de l'audio d'un appel sur les fins de transfert, comme avec un vrai contrôleur
/// USB : un envoi de N paquets est terminé quand ses N trames (1 ms chacune en pleine vitesse) sont passées.
/// Si le relais répondait tout de suite, Windows enverrait trop vite, puis devrait sauter des morceaux.</para>
/// <para>usbip-win2 0.9.8.1 numérote les trames en millisecondes depuis le démarrage de Windows (le temps
/// d'interruption divisé par 10 000), sur 32 bits. Tant que la trame demandée est proche de l'heure actuelle
/// (moins de 1 s d'écart), on la suit. Sinon (autre version d'usbip-win2), les envois s'enchaînent bout à bout,
/// comme un contrôleur place un transfert « dès que possible ». Comportement relevé par WPods.</para>
/// Un seul thread l'appelle (celui qui soumet les envois).
/// </remarks>
public sealed class IsoOutClock
{
    /// <summary>Écart maximal entre trame demandée et heure actuelle pour croire qu'il s'agit de l'heure système.</summary>
    public const long SystemTimeToleranceMs = 1000;

    private bool? _systemTime;
    private long _nextFrame;

    /// <summary>null tant qu'aucun envoi daté n'est passé ; sinon true si les trames suivent l'heure système.</summary>
    public bool? UsesSystemTime => _systemTime;

    /// <summary>
    /// La trame après laquelle un envoi de <paramref name="packets"/> paquets, demandé pour la trame
    /// <paramref name="askedStart"/> (0 = dès que possible), est terminé.
    /// </summary>
    /// <param name="askedStart">Trame de départ demandée par Windows (32 bits).</param>
    /// <param name="packets">Nombre de paquets (une trame chacun).</param>
    /// <param name="now">Trame actuelle, en millisecondes depuis le démarrage.</param>
    public long EndFrame(uint askedStart, int packets, long now)
    {
        // Les numéros sont sur 32 bits : on prend celui qui est le plus proche de maintenant.
        long nearest = now + unchecked((int)(askedStart - (uint)now));
        if (_systemTime is null && askedStart != 0) _systemTime = Math.Abs(nearest - now) <= SystemTimeToleranceMs;
        long start = _systemTime == true && askedStart != 0 ? nearest : Math.Max(_nextFrame, now + 1);
        _nextFrame = start + packets;
        return _nextFrame;
    }
}
