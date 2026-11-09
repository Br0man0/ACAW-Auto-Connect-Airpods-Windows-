using Acaw.Core.Hosting;

namespace Acaw.Core.Config;

/// <summary>La configuration rangée dans un fichier JSON (par défaut %APPDATA%\ACAW\config.json).</summary>
public sealed class FileConfigSource(string path) : IConfigSource
{
    public string Path => path;

    public AcawConfig Load() => ConfigStore.Load(path);

    public void Save(AcawConfig config) => ConfigStore.Save(path, config);
}
