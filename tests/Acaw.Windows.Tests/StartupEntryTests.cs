using Acaw.Windows;
using Microsoft.Win32;

namespace Acaw.Windows.Tests;

/// <summary>
/// « Démarrer avec Windows ». Chaque test travaille dans sa propre clé temporaire de HKCU, supprimée à la
/// fin : le vrai démarrage de l'utilisateur n'est jamais touché. Pas de Trait « Windows » : le registre
/// existe aussi sur la machine de GitHub, ces tests y tournent donc.
/// </summary>
public sealed class StartupEntryTests : IDisposable
{
    private const string Exe = @"C:\Users\Romsy\AppData\Local\Programs\ACAW\acaw-tray.exe";
    private static readonly byte[] Disabled = [0x03, 0, 0, 0, 0x8E, 0x2A, 0x5B, 0x47, 0x6D, 0x3C, 0xDB, 0x01];

    private readonly string _path = $@"Software\ACAW.Tests\{Guid.NewGuid():N}";
    private readonly RegistryKey _root;

    public StartupEntryTests() => _root = Registry.CurrentUser.CreateSubKey(_path);

    public void Dispose()
    {
        _root.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(_path, throwOnMissingSubKey: false);
    }

    [Fact]
    public void Rien_dans_le_registre_donne_desactive()
    {
        Assert.False(new StartupEntry(Exe, _root).IsEnabled);
    }

    [Fact]
    public void Activer_ecrit_la_commande_et_l_autorisation()
    {
        var entry = new StartupEntry(Exe, _root);

        entry.Set(true);

        Assert.True(entry.IsEnabled);
        Assert.Equal($"\"{Exe}\"", Read(StartupEntry.RunKey));
        Assert.Equal(0x02, Assert.IsType<byte[]>(Read(StartupEntry.ApprovedKey))[0]);
    }

    [Fact]
    public void Commande_sans_autorisation_n_est_pas_activee()
    {
        // Le cas constaté le 2026-11-04 : la commande était là, mais Windows ne la lançait pas.
        Write(StartupEntry.RunKey, $"\"{Exe}\"");

        Assert.False(new StartupEntry(Exe, _root).IsEnabled);
    }

    [Fact]
    public void Desactive_dans_les_Parametres_n_est_pas_active_et_le_menu_le_reactive()
    {
        Write(StartupEntry.RunKey, $"\"{Exe}\"");
        Write(StartupEntry.ApprovedKey, Disabled);
        var entry = new StartupEntry(Exe, _root);

        Assert.False(entry.IsEnabled);
        entry.Set(true);
        Assert.True(entry.IsEnabled);
    }

    [Fact]
    public void Commande_vers_un_autre_dossier_n_est_pas_activee_et_le_menu_la_corrige()
    {
        // Le programme a été déplacé (dossier de compilation → dossier d'installation).
        Write(StartupEntry.RunKey, @"""C:\Projets\ACAW\bin\Release\publish\acaw-tray.exe""");
        Write(StartupEntry.ApprovedKey, (byte[])[0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
        var entry = new StartupEntry(Exe, _root);

        Assert.False(entry.IsEnabled);
        entry.Set(true);
        Assert.Equal($"\"{Exe}\"", Read(StartupEntry.RunKey));
    }

    [Fact]
    public void Chemin_compare_sans_guillemets_ni_majuscules()
    {
        Write(StartupEntry.RunKey, Exe.ToUpperInvariant()); // écrit à la main, sans guillemets
        Write(StartupEntry.ApprovedKey, (byte[])[0x06, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]); // autre état « activé »

        Assert.True(new StartupEntry(Exe, _root).IsEnabled);
    }

    [Fact]
    public void Desactiver_retire_nos_deux_valeurs_et_rien_d_autre()
    {
        var entry = new StartupEntry(Exe, _root);
        entry.Set(true);
        using (var run = _root.CreateSubKey(StartupEntry.RunKey)) run.SetValue("OneDrive", "onedrive.exe");

        entry.Set(false);

        Assert.False(entry.IsEnabled);
        Assert.Null(Read(StartupEntry.RunKey));
        Assert.Null(Read(StartupEntry.ApprovedKey));
        using var after = _root.OpenSubKey(StartupEntry.RunKey);
        Assert.Equal("onedrive.exe", after?.GetValue("OneDrive"));
    }

    [Fact]
    public void Desactiver_deux_fois_ne_leve_pas_d_erreur()
    {
        var entry = new StartupEntry(Exe, _root);

        entry.Set(false);
        entry.Set(false);

        Assert.False(entry.IsEnabled);
    }

    private object? Read(string key)
    {
        using var k = _root.OpenSubKey(key);
        return k?.GetValue(StartupEntry.ValueName);
    }

    private void Write(string key, object value)
    {
        using var k = _root.CreateSubKey(key);
        k.SetValue(StartupEntry.ValueName, value);
    }
}
