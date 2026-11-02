namespace Acaw.Core.Switching;

/// <summary>Ce que la machine à états demande à l'hôte d'exécuter.</summary>
public enum SwitchCommand
{
    /// <summary>Connecter les AirPods au PC (KSPROPERTY_ONESHOT_RECONNECT).</summary>
    Reconnect,

    /// <summary>Relâcher les AirPods pour que l'iPhone les reprenne (KSPROPERTY_ONESHOT_DISCONNECT).</summary>
    Disconnect,

    /// <summary>Mettre en pause les médias du PC (règle R6 de l'ADR-0003).</summary>
    PauseMedia,
}
