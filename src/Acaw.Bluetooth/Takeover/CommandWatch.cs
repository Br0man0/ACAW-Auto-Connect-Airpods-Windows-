namespace Acaw.Bluetooth.Takeover;

/// <summary>
/// Après un réveil du PC, repère un adaptateur qui a perdu son firmware : une commande HCI reste sans aucune
/// réponse. Il faut alors rebrancher l'adaptateur virtuel, pour que le pilote de Windows recharge le firmware.
/// </summary>
/// <remarks>
/// Sans décoder le HCI : en USB, une commande HCI est un transfert de contrôle « classe, vers le périphérique »
/// (bmRequestType 0x20), et les réponses (événements) arrivent sur le point de terminaison d'interruption. La
/// surveillance ne dure que <see cref="ResumeWatch"/> après le réveil : en marche normale, rien n'est surveillé.
/// Peut être appelé depuis plusieurs threads (un par tuyau USB).
/// </remarks>
public sealed class CommandWatch(TimeProvider time)
{
    public static readonly TimeSpan ResumeWatch = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan UnansweredLimit = TimeSpan.FromSeconds(3);

    private readonly Lock _lock = new();
    private DateTimeOffset? _watchUntil;
    private DateTimeOffset? _pendingSince;

    /// <summary>true si le paquet de configuration (8 octets) est celui d'une commande HCI.</summary>
    public static bool IsHciCommand(ReadOnlySpan<byte> setup) => setup.Length >= 8 && setup[0] == 0x20;

    /// <summary>Le PC vient de se réveiller : surveiller pendant <see cref="ResumeWatch"/>.</summary>
    public void Arm()
    {
        lock (_lock)
        {
            _watchUntil = time.GetUtcNow() + ResumeWatch;
            _pendingSince = null;
        }
    }

    /// <summary>Une commande HCI part vers l'adaptateur.</summary>
    public void OnCommandSent()
    {
        lock (_lock)
        {
            if (Watching(time.GetUtcNow())) _pendingSince ??= time.GetUtcNow();
        }
    }

    /// <summary>Un événement HCI est arrivé : l'adaptateur répond.</summary>
    public void OnEvent()
    {
        lock (_lock) _pendingSince = null;
    }

    /// <summary>true si une commande attend une réponse depuis plus de <see cref="UnansweredLimit"/> pendant la surveillance.</summary>
    public bool FirmwareLost()
    {
        lock (_lock)
        {
            DateTimeOffset now = time.GetUtcNow();
            return _pendingSince is { } since && now - since >= UnansweredLimit && since <= _watchUntil;
        }
    }

    private bool Watching(DateTimeOffset now) => _watchUntil is { } until && now <= until;
}
