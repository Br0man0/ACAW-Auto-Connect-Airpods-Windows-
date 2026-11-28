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

/// <summary>La liaison radio avec les AirPods s'est établie (ils arrivent, ou sont là).</summary>
public sealed record RadioLinkUp : SwitchEvent;

/// <summary>La liaison radio avec les AirPods est coupée.</summary>
public sealed record RadioLinkDown : SwitchEvent;

/// <summary>L'état de la liaison radio est devenu illisible : la machine revient à la limite fixe (MuteTimeout).</summary>
public sealed record RadioLinkUnknown : SwitchEvent;

/// <summary>Une annonce BLE des AirPods vient d'être reçue : ils sont éveillés.</summary>
public sealed record AirPodsSeen : SwitchEvent;

/// <summary>
/// ACAW vient de démarrer, ou le PC sort de veille : Windows va peut-être reconnecter les AirPods de lui-même.
/// Ouvre la fenêtre de démarrage (<see cref="SwitchOptions.StartupWindow"/>).
/// </summary>
public sealed record Started : SwitchEvent;

/// <summary>Battement régulier envoyé par l'hôte pour faire avancer les minuteurs.</summary>
public sealed record Tick : SwitchEvent;

/// <summary>
/// La case « Bascule automatique » change. Décochée (règle M, mode manuel) : ACAW ne déplace plus jamais les
/// AirPods de lui-même, seuls « Amener sur ce PC » et « Rendre » le font. Recochée : les règles normales
/// s'appliquent tout de suite à l'état du moment.
/// </summary>
public sealed record AutomaticChanged(bool On) : SwitchEvent;
