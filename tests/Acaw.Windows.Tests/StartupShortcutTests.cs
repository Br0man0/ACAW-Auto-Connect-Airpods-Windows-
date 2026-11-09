using Acaw.Windows;
using Microsoft.Win32;

namespace Acaw.Windows.Tests;

/// <summary>
/// « Démarrer avec Windows ». Chaque test a son propre dossier Démarrage et sa propre clé de registre,
/// temporaires et supprimés à la fin : le vrai démarrage de l'utilisateur n'est jamais touché. Pas de Trait
/// « Windows » : les raccourcis et le registre existent aussi sur la machine de GitHub, ces tests y tournent.
/// </summary>
public sealed class StartupShortcutTests : IDisposable
{
    private static readonly byte[] Enabled = [0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
    private static readonly byte[] Disabled = [0x03, 0, 0, 0, 0x8E, 0x2A, 0x5B, 0x47, 0x6D, 0x3C, 0xDB, 0x01];

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"acaw-test-{Guid.NewGuid():N}");
    private readonly string _startup;
    private readonly string _exe;
    private const string TestsKey = @"Software\ACAW.Tests";
    private readonly string _keyPath = $@"{TestsKey}\{Guid.NewGuid():N}";
    private readonly RegistryKey _root;

    public StartupShortcutTests()
    {
        _startup = Path.Combine(_dir, "Démarrage");
        _exe = Path.Combine(_dir, "Programs", "ACAW", "acaw-tray.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(_exe)!);
        File.WriteAllText(_exe, ""); // la cible doit exister pour qu'un raccourci s'y résolve comme le vrai
        _root = Registry.CurrentUser.CreateSubKey(_keyPath);
    }

    public void Dispose()
    {
        _root.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(_keyPath, throwOnMissingSubKey: false);
        using (var parent = Registry.CurrentUser.OpenSubKey(TestsKey))
            if (parent?.SubKeyCount == 0) Registry.CurrentUser.DeleteSubKey(TestsKey, throwOnMissingSubKey: false);
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private StartupShortcut Shortcut(string? exe = null) => new(exe ?? _exe, _startup, _root);

    [Fact]
    public void Sans_raccourci_le_demarrage_est_desactive()
    {
        Assert.False(Shortcut().IsEnabled);
    }

    [Fact]
    public void Activer_cree_un_raccourci_vers_le_programme()
    {
        var shortcut = Shortcut();

        shortcut.Set(true);

        Assert.True(File.Exists(Path.Combine(_startup, StartupShortcut.FileName)));
        Assert.True(shortcut.IsEnabled);
    }

    [Fact]
    public void Raccourci_vers_un_autre_dossier_n_est_pas_active_et_le_menu_le_corrige()
    {
        // Le programme a été déplacé : l'ancien raccourci lancerait l'ancienne copie.
        string old = Path.Combine(_dir, "bin", "acaw-tray.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(old)!);
        File.WriteAllText(old, "");
        Shortcut(old).Set(true);
        var shortcut = Shortcut();

        Assert.False(shortcut.IsEnabled);
        shortcut.Set(true);
        Assert.True(shortcut.IsEnabled);
    }

    [Fact]
    public void Desactive_dans_les_Parametres_n_est_pas_active_et_le_menu_le_reactive()
    {
        var shortcut = Shortcut();
        shortcut.Set(true);
        Write(StartupShortcut.ApprovedKey, StartupShortcut.FileName, Disabled);

        Assert.False(shortcut.IsEnabled);
        shortcut.Set(true);
        Assert.True(shortcut.IsEnabled);
        Assert.Null(Read(StartupShortcut.ApprovedKey, StartupShortcut.FileName)); // absente = activé
    }

    [Fact]
    public void Active_dans_les_Parametres_reste_active()
    {
        var shortcut = Shortcut();
        shortcut.Set(true);
        Write(StartupShortcut.ApprovedKey, StartupShortcut.FileName, Enabled);

        Assert.True(shortcut.IsEnabled);
    }

    [Fact]
    public void Fichier_abime_compte_comme_absent()
    {
        Directory.CreateDirectory(_startup);
        File.WriteAllText(Path.Combine(_startup, StartupShortcut.FileName), "pas un raccourci");

        Assert.False(Shortcut().IsEnabled);
    }

    [Fact]
    public void Desactiver_supprime_le_raccourci_et_rien_d_autre()
    {
        var shortcut = Shortcut();
        shortcut.Set(true);
        string other = Path.Combine(_startup, "Autre.lnk");
        File.WriteAllText(other, "");

        shortcut.Set(false);

        Assert.False(shortcut.IsEnabled);
        Assert.False(File.Exists(Path.Combine(_startup, StartupShortcut.FileName)));
        Assert.True(File.Exists(other));
    }

    [Fact]
    public void Desactiver_deux_fois_ne_leve_pas_d_erreur()
    {
        var shortcut = Shortcut();

        shortcut.Set(false);
        shortcut.Set(false);

        Assert.False(shortcut.IsEnabled);
    }

    private object? Read(string key, string name)
    {
        using var k = _root.OpenSubKey(key);
        return k?.GetValue(name);
    }

    private void Write(string key, string name, object value)
    {
        using var k = _root.CreateSubKey(key);
        k.SetValue(name, value);
    }
}
