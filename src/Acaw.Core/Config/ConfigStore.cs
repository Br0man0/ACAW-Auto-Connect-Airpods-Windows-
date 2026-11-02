using System.Text.Encodings.Web;
using System.Text.Json;

namespace Acaw.Core.Config;

/// <summary>Lecture et écriture de la configuration au format JSON.</summary>
public static class ConfigStore
{
    // Encodeur « relâché » : garde les accents lisibles dans le fichier (les emojis restent en \uXXXX).
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>%APPDATA%\ACAW\config.json : propre à l'utilisateur, hors du dépôt.</summary>
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ACAW", "config.json");

    /// <summary>Renvoie la configuration enregistrée, ou une configuration vide si le fichier n'existe pas.</summary>
    /// <exception cref="InvalidDataException">Le fichier existe mais n'est pas du JSON valide (modifié à la main ?).</exception>
    public static AcawConfig Load(string path)
    {
        if (!File.Exists(path)) return new AcawConfig();
        AcawConfig config;
        try
        {
            config = JsonSerializer.Deserialize<AcawConfig>(File.ReadAllText(path), Json) ?? new AcawConfig();
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"Configuration illisible ({path}) : {e.Message}", e);
        }

        // Un délai négatif ferait planter les attentes plus loin : on le refuse ici, avec un message clair.
        var o = config.Switching ?? new();
        TimeSpan[] delays = [o.PauseDelay, o.SilenceDelay, o.Cooldown, o.AwakeWindow, o.SwitchTimeout,
                             o.SoundConfirmDelay, o.SilenceConfirmDelay, o.UnmuteDelay, o.MaxMuteDuration];
        if (delays.Any(d => d < TimeSpan.Zero))
            throw new InvalidDataException($"Configuration illisible ({path}) : un délai est négatif.");
        return config with { Switching = o };
    }

    public static void Save(string path, AcawConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(config, Json));
    }
}
