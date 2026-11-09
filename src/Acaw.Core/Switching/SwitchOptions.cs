namespace Acaw.Core.Switching;

/// <summary>Délais de la machine à états. Les valeurs par défaut sont justifiées dans l'ADR-0003.</summary>
public sealed record SwitchOptions
{
    /// <summary>T_pause : délai entre une pause explicite et la relâche des AirPods.</summary>
    public TimeSpan PauseDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>T_silence : délai entre un silence sans pause et la relâche.</summary>
    public TimeSpan SilenceDelay { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>T_cooldown : après une perte, délai minimal avant de pouvoir reprendre les AirPods.</summary>
    public TimeSpan Cooldown { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>T_eveil : AirPods considérés éveillés si une annonce BLE a été reçue depuis moins de ce délai.</summary>
    public TimeSpan AwakeWindow { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Temps maximal d'attente d'une connexion ou d'une déconnexion.</summary>
    public TimeSpan SwitchTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Un son hors lecteur média (jeu, Discord) ne compte qu'après avoir duré ce délai : écarte les notifications.</summary>
    public TimeSpan SoundConfirmDelay { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Un son est considéré terminé après ce délai de silence continu.</summary>
    public TimeSpan SilenceConfirmDelay { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>R7 : couper les haut-parleurs pendant l'arrivée des AirPods, pour que le son n'y sorte pas.</summary>
    public bool MuteWhileSwitching { get; init; } = true;

    /// <summary>Une fois les AirPods connectés, délai avant de rétablir les haut-parleurs (le temps que Windows bascule le son).</summary>
    public TimeSpan UnmuteDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Durée maximale de la coupure : si les AirPods ne viennent pas, le son revient sur les haut-parleurs.
    /// Une bascule a été mesurée à 11,6 s (2026-11-09), et ~10–13 s avec le micro des AirPods : 20 s laisse de
    /// la marge. Remplace « MaxMuteDuration » (6 s, trop court) : l'ancienne clé, inscrite dans les config.json
    /// existants, est ignorée à la lecture, si bien que tout le monde passe à la nouvelle valeur.
    /// </summary>
    public TimeSpan MuteTimeout { get; init; } = TimeSpan.FromSeconds(20);
}
