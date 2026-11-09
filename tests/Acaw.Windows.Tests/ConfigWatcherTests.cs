using Acaw.Windows;
using Microsoft.Extensions.Time.Testing;

namespace Acaw.Windows.Tests;

/// <summary>
/// Surveillance de config.json. Le dossier de test est sous bin\ et non dans %TEMP% : lancés depuis un outil
/// empaqueté (l'application Claude), les accès à AppData, donc à %TEMP%, sont redirigés, et la surveillance
/// de fichiers pourrait s'y comporter autrement. Pas de Trait « Windows » : ces tests tournent aussi sur GitHub.
/// </summary>
public sealed class ConfigWatcherTests : IDisposable
{
    private readonly string _dir = Path.Combine(AppContext.BaseDirectory, "test-watch", Guid.NewGuid().ToString("N"));
    private string ConfigPath => Path.Combine(_dir, "ACAW", "config.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Rafale_d_ecritures_ne_donne_qu_une_relecture_une_fois_le_calme_revenu()
    {
        var time = new FakeTimeProvider();
        using var watcher = new ConfigWatcher(ConfigPath, time);
        int changes = 0;
        watcher.Changed += () => changes++;

        for (int i = 0; i < 5; i++) // un éditeur qui écrit en cinq fois, à 100 ms d'écart
        {
            watcher.Notify();
            time.Advance(TimeSpan.FromMilliseconds(100));
        }
        Assert.Equal(0, changes); // pas encore calme

        time.Advance(ConfigWatcher.QuietDelay);
        Assert.Equal(1, changes);

        time.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(1, changes); // et rien de plus ensuite
    }

    [Fact]
    public void Sans_ecriture_rien_n_est_releve()
    {
        var time = new FakeTimeProvider();
        using var watcher = new ConfigWatcher(ConfigPath, time);
        int changes = 0;
        watcher.Changed += () => changes++;

        time.Advance(TimeSpan.FromMinutes(1));

        Assert.Equal(0, changes);
    }

    [Fact]
    public void Dossier_absent_au_premier_lancement_est_cree()
    {
        using var watcher = new ConfigWatcher(ConfigPath, new FakeTimeProvider());

        Assert.True(Directory.Exists(Path.GetDirectoryName(ConfigPath)));
    }

    [Fact]
    public async Task Fichier_ecrit_sur_le_disque_est_vu()
    {
        using var watcher = new ConfigWatcher(ConfigPath, quietDelay: TimeSpan.FromMilliseconds(50));
        var seen = new TaskCompletionSource();
        watcher.Changed += () => seen.TrySetResult();

        await File.WriteAllTextAsync(ConfigPath, """{ "AutoSwitch": false }""");

        await seen.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Enregistrement_par_fichier_temporaire_renomme_est_vu()
    {
        using var watcher = new ConfigWatcher(ConfigPath, quietDelay: TimeSpan.FromMilliseconds(50));
        var seen = new TaskCompletionSource();
        watcher.Changed += () => seen.TrySetResult();
        string temp = ConfigPath + ".tmp";

        await File.WriteAllTextAsync(temp, "{}");
        File.Move(temp, ConfigPath); // ce que font certains éditeurs pour ne jamais laisser un fichier à moitié écrit

        await seen.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
