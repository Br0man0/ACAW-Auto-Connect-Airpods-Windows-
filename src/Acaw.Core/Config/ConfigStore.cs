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
    public static AcawConfig Load(string path) =>
        File.Exists(path)
            ? JsonSerializer.Deserialize<AcawConfig>(File.ReadAllText(path), Json) ?? new AcawConfig()
            : new AcawConfig();

    public static void Save(string path, AcawConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(config, Json));
    }
}
