using Microsoft.Win32;
using Windows.Win32;
using Windows.Win32.Storage.FileSystem;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Shell;

namespace Acaw.Windows;

/// <summary>
/// « Démarrer avec Windows » : un raccourci ACAW.lnk dans le dossier Démarrage de l'utilisateur
/// (shell:startup). Aucun droit administrateur nécessaire, et on peut le voir ou le supprimer à la main.
/// (La clé Run de HKCU marcherait aussi ; le raccourci a l'avantage d'être visible.)
/// </summary>
/// <param name="executablePath">Chemin de acaw-tray.exe.</param>
/// <param name="startupFolder">Dossier Démarrage : celui de l'utilisateur, ou un dossier temporaire dans les tests.</param>
/// <param name="root">Racine du registre : HKCU par défaut, une clé temporaire dans les tests.</param>
public sealed class StartupShortcut(string executablePath, string startupFolder, RegistryKey? root = null)
{
    public const string FileName = "ACAW.lnk";

    /// <summary>L'interrupteur de la page Paramètres → Applications → Démarrage pour le dossier Démarrage.</summary>
    public const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";

    private readonly RegistryKey _root = root ?? Registry.CurrentUser;

    /// <summary>Le raccourci de l'utilisateur courant : son dossier Démarrage, son registre (HKCU).</summary>
    public static StartupShortcut ForCurrentUser(string executablePath) =>
        new(executablePath, Environment.GetFolderPath(Environment.SpecialFolder.Startup));

    public string ShortcutPath => Path.Combine(startupFolder, FileName);

    /// <summary>
    /// true si Windows lancera ce programme à l'ouverture de session : le raccourci existe, il pointe vers
    /// ce programme (et non vers un ancien dossier), et l'utilisateur ne l'a pas désactivé dans les Paramètres.
    /// </summary>
    public bool IsEnabled =>
        File.Exists(ShortcutPath)
        && string.Equals(ReadTarget(ShortcutPath), executablePath, StringComparison.OrdinalIgnoreCase)
        && !DisabledInSettings();

    /// <summary>
    /// Crée ou supprime le raccourci. Dans les deux cas, on retire la valeur des Paramètres (absente = activé) :
    /// cocher la case est une demande explicite, qui prime sur une ancienne désactivation.
    /// </summary>
    public void Set(bool enabled)
    {
        if (enabled)
        {
            Directory.CreateDirectory(startupFolder);
            WriteShortcut();
        }
        else if (File.Exists(ShortcutPath))
        {
            File.Delete(ShortcutPath); // File.Delete seul lèverait une erreur si le dossier n'existe pas
        }
        using var approved = _root.OpenSubKey(ApprovedKey, writable: true);
        approved?.DeleteValue(FileName, throwOnMissingValue: false);
    }

    private void WriteShortcut()
    {
        var link = (IShellLinkW)new ShellLink();
        link.SetPath(executablePath);
        link.SetWorkingDirectory(Path.GetDirectoryName(executablePath)!);
        link.SetDescription("ACAW : bascule automatique des AirPods");
        ((IPersistFile)link).Save(ShortcutPath, true);
    }

    // Cible du raccourci, ou null s'il est illisible (abîmé, ou pas un raccourci) : il compte alors comme absent.
    private static string? ReadTarget(string shortcut)
    {
        try
        {
            var link = (IShellLinkW)new ShellLink();
            ((IPersistFile)link).Load(shortcut, STGM.STGM_READ);
            Span<char> buffer = stackalloc char[1024];
            var data = default(WIN32_FIND_DATAW);
            link.GetPath(buffer, ref data, 0).ThrowOnFailure();
            int end = buffer.IndexOf('\0');
            return new string(end < 0 ? buffer : buffer[..end]);
        }
        catch (Exception e) when (Com.IsComFailure(e))
        {
            return null;
        }
    }

    // Format de Windows : le premier octet dit l'état, bit 0 à 1 = désactivé (03, ou 07 par une stratégie).
    private bool DisabledInSettings()
    {
        using var approved = _root.OpenSubKey(ApprovedKey);
        return approved?.GetValue(FileName) is byte[] { Length: > 0 } state && (state[0] & 1) != 0;
    }
}
