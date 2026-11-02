using Acaw.Core.Config;
using Acaw.Core.Switching;

namespace Acaw.Core.Tests;

/// <summary>
/// Chaque test travaille dans son propre dossier temporaire, supprimé à la fin même si le test échoue :
/// xUnit appelle Dispose après chaque test.
/// </summary>
public sealed class ConfigStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"acaw-test-{Guid.NewGuid():N}");
    private string ConfigPath => Path.Combine(_dir, "config.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Fichier_absent_donne_une_configuration_vide()
    {
        var config = ConfigStore.Load(ConfigPath);

        Assert.False(config.IsConfigured);
        Assert.True(config.AutoSwitch);
    }

    [Fact]
    public void Enregistre_puis_relit_la_meme_configuration()
    {
        var saved = new AcawConfig
        {
            Mac = "A1:B2:C3:D4:E5:F6",
            ModelId = "2027",
            Name = "AirPods Pro",
            AutoSwitch = false,
            Switching = new SwitchOptions { PauseDelay = TimeSpan.FromSeconds(8) },
        };

        ConfigStore.Save(ConfigPath, saved);

        Assert.Equal(saved, ConfigStore.Load(ConfigPath));
    }

    [Fact]
    public void Fichier_abime_leve_une_erreur_explicite()
    {
        Write("{ \"Mac\": ");

        var error = Assert.Throws<InvalidDataException>(() => ConfigStore.Load(ConfigPath));
        Assert.Contains("Configuration illisible", error.Message);
    }

    [Fact]
    public void Delai_negatif_est_refuse()
    {
        Write("""{ "Mac": "A1:B2:C3:D4:E5:F6", "Switching": { "PauseDelay": "-00:00:05" } }""");

        var error = Assert.Throws<InvalidDataException>(() => ConfigStore.Load(ConfigPath));
        Assert.Contains("négatif", error.Message);
    }

    [Fact]
    public void Champs_absents_prennent_les_valeurs_par_defaut()
    {
        // Configuration écrite par une version plus ancienne : sans AutoSwitch ni les nouveaux délais.
        Write("""{ "Mac": "A1:B2:C3:D4:E5:F6", "ModelId": "2027", "Switching": { "PauseDelay": "00:00:08" } }""");

        var config = ConfigStore.Load(ConfigPath);

        Assert.True(config.AutoSwitch);
        Assert.Equal(TimeSpan.FromSeconds(8), config.Switching.PauseDelay);
        Assert.Equal(TimeSpan.FromSeconds(2), config.Switching.SoundConfirmDelay);
    }

    [Theory]
    [InlineData("2027", (ushort)0x2027)]
    [InlineData("200e", (ushort)0x200E)]
    [InlineData(null, null)]
    [InlineData("zzzz", null)]
    public void Convertit_le_modele_en_nombre(string? modelId, ushort? expected)
    {
        Assert.Equal(expected, new AcawConfig { ModelId = modelId }.ModelNumber);
    }

    private void Write(string json)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(ConfigPath, json);
    }
}
