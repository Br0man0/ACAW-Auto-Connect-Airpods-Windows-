using Acaw.Core.Config;
using Acaw.Core.Switching;

namespace Acaw.Core.Tests;

public class ConfigStoreTests
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"acaw-test-{Guid.NewGuid():N}", "config.json");

    [Fact]
    public void Fichier_absent_donne_une_configuration_vide()
    {
        var config = ConfigStore.Load(_path);

        Assert.False(config.IsConfigured);
    }

    [Fact]
    public void Enregistre_puis_relit_la_meme_configuration()
    {
        var saved = new AcawConfig
        {
            Mac = "A1:B2:C3:D4:E5:F6",
            ModelId = "2027",
            Name = "AirPods Pro",
            Switching = new SwitchOptions { PauseDelay = TimeSpan.FromSeconds(8) },
        };

        ConfigStore.Save(_path, saved);
        var loaded = ConfigStore.Load(_path);

        Assert.Equal(saved, loaded);
        Directory.Delete(Path.GetDirectoryName(_path)!, recursive: true);
    }
}
