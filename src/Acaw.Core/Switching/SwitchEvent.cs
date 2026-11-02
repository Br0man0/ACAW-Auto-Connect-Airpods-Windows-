namespace Acaw.Core.Switching;

/// <summary>
/// Ce qui arrive au monde extérieur et que la machine à états doit prendre en compte.
/// Les événements sont produits par les adaptateurs Windows (son, Bluetooth, BLE) ou par l'utilisateur.
/// </summary>
public abstract record SwitchEvent;

/// <summary>Le PC passe de « rien ne joue » à « quelque chose joue ».</summary>
public sealed record PlaybackStarted : SwitchEvent;

/// <summary>L'utilisateur a mis en pause (clic, touche média, appui sur l'AirPod) : signal volontaire.</summary>
public sealed record PlaybackPaused : SwitchEvent;

/// <summary>Plus aucun son, sans pause explicite (jeu, Discord, appli sans contrôles média).</summary>
public sealed record SilenceDetected : SwitchEvent;

/// <summary>L'utilisateur demande de rendre les AirPods (bouton ou raccourci).</summary>
public sealed record ReleaseRequested : SwitchEvent;

/// <summary>
/// L'utilisateur demande d'amener les AirPods sur ce PC (menu « Amener sur ce PC »). Demande explicite :
/// elle passe outre le cooldown et l'éveil BLE, et les AirPods restent jusqu'à la fin de la prochaine lecture.
/// </summary>
public sealed record TakeRequested : SwitchEvent;

/// <summary>L'endpoint audio des AirPods est devenu actif sur ce PC.</summary>
public sealed record EndpointConnected : SwitchEvent;

/// <summary>L'endpoint audio des AirPods n'est plus actif sur ce PC.</summary>
public sealed record EndpointDisconnected : SwitchEvent;

/// <summary>Une annonce BLE des AirPods vient d'être reçue : ils sont éveillés.</summary>
public sealed record AirPodsSeen : SwitchEvent;

/// <summary>Battement régulier envoyé par l'hôte pour faire avancer les minuteurs.</summary>
public sealed record Tick : SwitchEvent;
