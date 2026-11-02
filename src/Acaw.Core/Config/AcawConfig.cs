using System.Text.Json.Serialization;
using Acaw.Core.Switching;

namespace Acaw.Core.Config;

/// <summary>Réglages d'ACAW, enregistrés par « acaw setup ».</summary>
public sealed record AcawConfig
{
    /// <summary>Adresse MAC des AirPods choisis (ex. « A1:B2:C3:D4:E5:F6 »).</summary>
    public string? Mac { get; init; }

    /// <summary>Modèle des AirPods, qui sert à reconnaître leurs annonces BLE (ex. « 2027 »).</summary>
    public string? ModelId { get; init; }

    /// <summary>Nom affiché par Windows, pour information seulement.</summary>
    public string? Name { get; init; }

    public SwitchOptions Switching { get; init; } = new();

    [JsonIgnore] // calculé, pas enregistré
    public bool IsConfigured => Mac is not null;
}
