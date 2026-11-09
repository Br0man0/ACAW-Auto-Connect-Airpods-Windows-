using System.Globalization;
using System.Text.Json.Serialization;
using Acaw.Core.Switching;

namespace Acaw.Core.Config;

/// <summary>Réglages d'ACAW, enregistrés par « acaw setup » et par le menu de la barre des tâches.</summary>
public sealed record AcawConfig
{
    /// <summary>Adresse MAC des AirPods choisis (ex. « A1:B2:C3:D4:E5:F6 »).</summary>
    public string? Mac { get; init; }

    /// <summary>Modèle des AirPods, qui sert à reconnaître leurs annonces BLE (ex. « 2027 »).</summary>
    public string? ModelId { get; init; }

    /// <summary>Nom affiché par Windows, pour information seulement.</summary>
    public string? Name { get; init; }

    /// <summary>Bascule automatique active. Le menu permet de la suspendre sans quitter l'application.</summary>
    public bool AutoSwitch { get; init; } = true;

    /// <summary>
    /// L'utilisateur a choisi d'activer le micro des AirPods (profil mains libres) depuis ACAW : les bascules
    /// sont lentes, mais c'est voulu, donc pas d'avertissement « bascules lentes » au démarrage.
    /// </summary>
    public bool Microphone { get; init; }

    public SwitchOptions Switching { get; init; } = new();

    [JsonIgnore] // calculé, pas enregistré
    public bool IsConfigured => Mac is not null;

    /// <summary>Le modèle sous forme de nombre (« 2027 » → 0x2027), comme dans les trames BLE ; null si absent ou invalide.</summary>
    [JsonIgnore]
    public ushort? ModelNumber =>
        ushort.TryParse(ModelId, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var model) ? model : null;
}
