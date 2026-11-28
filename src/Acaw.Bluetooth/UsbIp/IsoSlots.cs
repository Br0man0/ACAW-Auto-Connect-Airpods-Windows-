namespace Acaw.Bluetooth.UsbIp;

/// <summary>
/// Les cases du tampon isochrone : une par transfert en vol. Une case n'est jamais redonnée tant que son
/// transfert n'est pas terminé.
/// </summary>
/// <remarks>
/// Revue W1-6, point 2 : avant, la case suivante était prise « au tour suivant », même si son transfert était
/// encore attendu par WinUSB ; avec toutes les cases en vol, un nouveau transfert réécrivait les données et la
/// structure d'attente d'un transfert en cours. Le tour est gardé (les cases tournent, comme avant), mais une
/// case occupée est sautée, et s'il n'y en a plus de libre, l'appelant doit attendre.
/// Pas de verrou ici : l'appelant protège les appels (le tuyau isochrone le fait).
/// </remarks>
public sealed class IsoSlots(int count)
{
    private readonly bool[] _busy = new bool[count];
    private int _next;

    /// <summary>Nombre de cases.</summary>
    public int Count => _busy.Length;

    /// <summary>Nombre de cases occupées.</summary>
    public int Busy => _busy.Count(b => b);

    /// <summary>Prend la prochaine case libre (à partir du tour), ou null si toutes sont occupées.</summary>
    public int? TryTake()
    {
        for (int i = 0; i < _busy.Length; i++)
        {
            int slot = (_next + i) % _busy.Length;
            if (_busy[slot]) continue;
            _busy[slot] = true;
            _next = (slot + 1) % _busy.Length;
            return slot;
        }
        return null;
    }

    /// <summary>Libère la case d'un transfert terminé.</summary>
    /// <exception cref="InvalidOperationException">La case n'était pas occupée (erreur de programmation).</exception>
    public void Release(int slot)
    {
        if (!_busy[slot]) throw new InvalidOperationException($"La case {slot} n'était pas occupée.");
        _busy[slot] = false;
    }
}
