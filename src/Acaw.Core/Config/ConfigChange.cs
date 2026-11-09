namespace Acaw.Core.Config;

/// <summary>Ce qu'il faut refaire quand la configuration change.</summary>
public enum ConfigChange
{
    /// <summary>Rien (identique, ou seuls le nom affiché ou le choix du micro ont changé).</summary>
    Nothing,

    /// <summary>Seule la bascule automatique a changé : l'hôte en marche l'applique directement.</summary>
    AutoSwitch,

    /// <summary>Autre paire, autre modèle ou autres délais : il faut recréer l'hôte.</summary>
    Restart,
}

public static class ConfigChanges
{
    /// <param name="running">La configuration de l'hôte en marche, ou null s'il n'y en a pas.</param>
    /// <param name="next">La nouvelle configuration.</param>
    public static ConfigChange Compare(AcawConfig? running, AcawConfig next)
    {
        if (running is null) return ConfigChange.Restart;
        // Le nom ne sert qu'à l'affichage, Microphone qu'à l'icône, et AutoSwitch s'applique sans redémarrer :
        // on les met de côté.
        var rest = running with { Name = next.Name, AutoSwitch = next.AutoSwitch, Microphone = next.Microphone };
        if (rest != next) return ConfigChange.Restart;
        return running.AutoSwitch == next.AutoSwitch ? ConfigChange.Nothing : ConfigChange.AutoSwitch;
    }
}
