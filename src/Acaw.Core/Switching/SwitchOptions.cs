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

    /// <summary>Durée maximale de la coupure : si les AirPods ne viennent pas, le son revient sur les haut-parleurs.</summary>
    public TimeSpan MaxMuteDuration { get; init; } = TimeSpan.FromSeconds(6);
}
