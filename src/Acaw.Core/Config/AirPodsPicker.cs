using System.Globalization;

namespace Acaw.Core.Config;

/// <summary>Un casque Bluetooth appairé au PC, vu par le Core (sans rien savoir de Windows).</summary>
/// <param name="Mac">Adresse Bluetooth (ex. « A1:B2:C3:D4:E5:F6 »).</param>
/// <param name="ModelId">Modèle (« pid » du driver, ex. « 2027 »), ou null s'il est inconnu.</param>
/// <param name="Name">Nom affiché par Windows.</param>
/// <param name="IsApple">true pour un appareil Apple : seuls ceux-là émettent les annonces BLE qu'ACAW comprend.</param>
public sealed record PairedHeadset(string Mac, string? ModelId, string Name, bool IsApple)
{
    /// <summary>Nom donné quand Windows ne sait pas (encore) lire celui de l'appareil, par ex. à l'ouverture de session.</summary>
    public const string UnreadableName = "(nom illisible)";

    public bool HasReadableName => !string.IsNullOrWhiteSpace(Name) && Name != UnreadableName;
}

/// <summary>Pourquoi <see cref="AirPodsPicker"/> a choisi, ou n'a pas choisi.</summary>
public enum PickReason
{
    /// <summary>La configuration désigne une paire toujours appairée : rien à changer.</summary>
    Configured,

    /// <summary>Rien n'était configuré, et une seule paire d'AirPods est appairée : elle est choisie.</summary>
    OnlyOne,

    /// <summary>La paire configurée n'est plus appairée, et une seule autre l'est : elle la remplace.</summary>
    Replaced,

    /// <summary>Rien n'est configuré, et aucune paire d'AirPods n'est appairée.</summary>
    NoneFound,

    /// <summary>Rien n'est configuré, et plusieurs paires sont appairées : à l'utilisateur de choisir.</summary>
    Several,

    /// <summary>La paire configurée n'est plus appairée, et il n'y a pas de remplaçante évidente.</summary>
    Orphan,
}

/// <param name="Choice">La paire à enregistrer, ou null si rien ne doit changer.</param>
public sealed record PickResult(PairedHeadset? Choice, PickReason Reason);

/// <summary>
/// Sélection automatique des AirPods. Règle d'or : ne choisir que s'il n'y a aucune ambiguïté (exactement
/// une paire possible). Sinon, ne rien changer et dire pourquoi.
/// </summary>
public static class AirPodsPicker
{
    /// <summary>Un casque qu'ACAW sait piloter : un appareil Apple dont on connaît l'adresse et le modèle.</summary>
    public static bool IsCandidate(PairedHeadset headset) => headset.IsApple && ParseModel(headset.ModelId) is not null;

    public static PickResult Pick(AcawConfig config, IReadOnlyList<PairedHeadset> paired)
    {
        var candidates = paired.Where(IsCandidate).ToList();
        if (IsUsable(config))
        {
            // Liste vide : au démarrage de Windows, le service audio peut ne rien lister encore. Ce n'est pas
            // la preuve que les AirPods ont disparu : on garde la configuration.
            if (paired.Count == 0 || paired.Any(h => SameMac(h.Mac, config.Mac))) return new(null, PickReason.Configured);
            return candidates.Count == 1 ? new(candidates[0], PickReason.Replaced) : new(null, PickReason.Orphan);
        }
        return candidates.Count switch
        {
            0 => new(null, PickReason.NoneFound),
            1 => new(candidates[0], PickReason.OnlyOne),
            _ => new(null, PickReason.Several),
        };
    }

    /// <summary>true si la configuration suffit pour démarrer : une adresse et un modèle valide.</summary>
    public static bool IsUsable(AcawConfig config) => config.IsConfigured && config.ModelNumber is not null;

    /// <summary>La configuration avec cette paire, les autres réglages (délais, bascule) étant conservés.</summary>
    public static AcawConfig WithHeadset(this AcawConfig config, PairedHeadset headset) =>
        config with { Mac = headset.Mac, ModelId = headset.ModelId, Name = headset.Name };

    public static bool SameMac(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static ushort? ParseModel(string? modelId) =>
        ushort.TryParse(modelId, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var model) ? model : null;
}
