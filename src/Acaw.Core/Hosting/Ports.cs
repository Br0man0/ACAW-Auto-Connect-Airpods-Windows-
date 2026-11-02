namespace Acaw.Core.Hosting;

// Les « ports » : ce dont l'hôte a besoin du monde extérieur, décrit sans rien savoir de Windows.
// Acaw.Windows les implémente avec les vraies API ; les tests les implémentent avec des faux.
// Les événements peuvent être levés depuis n'importe quel thread : l'hôte les remet dans sa file.

/// <summary>État du casque vu par Windows.</summary>
public enum HeadsetState
{
    /// <summary>Connecté à ce PC.</summary>
    Connected,

    /// <summary>Appairé mais pas connecté à ce PC (ailleurs ou libre).</summary>
    Disconnected,

    /// <summary>Inaccessible : Bluetooth coupé, ou casque plus appairé.</summary>
    Absent,
}

/// <summary>Le casque : son état, et les ordres de connexion / déconnexion.</summary>
public interface IHeadsetPort
{
    HeadsetState GetState();

    /// <summary>Demande la connexion. Rend la main tout de suite ; le résultat arrive par <see cref="StateChanged"/>.</summary>
    void Connect();

    /// <summary>Demande la déconnexion. Même fonctionnement que <see cref="Connect"/>.</summary>
    void Disconnect();

    event Action<HeadsetState>? StateChanged;
}

/// <summary>Les lecteurs média du PC (contrôles média de Windows).</summary>
public interface IMediaPort
{
    /// <summary>true si au moins un lecteur est en lecture.</summary>
    bool IsPlaying { get; }

    event Action<bool>? PlayingChanged;

    /// <summary>Met en pause tous les lecteurs en lecture (règle R6).</summary>
    void PauseAll();
}

/// <summary>Le volume crête du son des applications, hors sons système.</summary>
public interface ISoundMeter
{
    /// <summary>Volume crête actuel, de 0 (silence) à 1.</summary>
    float ReadPeak();
}

/// <summary>La sourdine de la sortie son actuelle (les haut-parleurs), règle R7.</summary>
public interface ISpeakers
{
    /// <summary>Coupe la sortie par défaut, sauf si l'utilisateur l'avait déjà coupée lui-même.</summary>
    void Mute();

    /// <summary>
    /// Rétablit la sortie que <see cref="Mute"/> a coupée, et seulement celle-là. Sans effet sinon.
    /// Appelé aussi au démarrage : si ACAW s'est arrêté brutalement pendant une coupure, le son revient.
    /// </summary>
    void Unmute();
}

/// <summary>L'écoute BLE des AirPods.</summary>
public interface IAirPodsRadar
{
    /// <summary>Levé à chaque annonce BLE des AirPods configurés.</summary>
    event Action? AirPodsSeen;

    /// <summary>Levé si l'écoute s'arrête d'elle-même (Bluetooth coupé…).</summary>
    event Action? Stopped;

    void Start();

    void Stop();
}
