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
}
