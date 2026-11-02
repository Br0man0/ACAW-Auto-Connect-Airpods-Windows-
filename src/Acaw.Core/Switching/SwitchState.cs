namespace Acaw.Core.Switching;

/// <summary>Où en sont les AirPods, du point de vue du PC.</summary>
public enum SwitchState
{
    /// <summary>Ailleurs (iPhone) ou libres. Point de départ.</summary>
    Away,

    /// <summary>Reconnexion demandée, en attente de l'endpoint actif.</summary>
    Switching,

    /// <summary>Connectés au PC.</summary>
    OnWindows,

    /// <summary>Déconnexion demandée, en attente de la confirmation.</summary>
    Releasing,

    /// <summary>Pris par un autre appareil sans qu'on l'ait demandé : pas de reprise immédiate (règle R5).</summary>
    Lost,
}
