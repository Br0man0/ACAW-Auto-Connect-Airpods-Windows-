using Microsoft.Win32;

namespace Acaw.Windows;

/// <summary>
/// « Démarrer avec Windows », dans la partie du registre propre à l'utilisateur (HKCU) : aucun droit
/// administrateur nécessaire, et rien n'est installé ailleurs. Il faut DEUX valeurs :
/// <list type="bullet">
/// <item>la commande, dans la clé Run ;</item>
/// <item>l'autorisation, dans StartupApproved\Run : c'est ce que règle la page Paramètres → Applications →
/// Démarrage. Sur le PC de Romsy, Windows ne lance une commande de Run que si elle y est marquée activée :
/// sans cette valeur, ACAW était ignoré à chaque ouverture de session (constaté le 2026-11-04).</item>
/// </list>
/// </summary>
/// <param name="executablePath">Chemin de acaw-tray.exe.</param>
/// <param name="root">Racine du registre : HKCU par défaut, une clé temporaire dans les tests.</param>
public sealed class StartupEntry(string executablePath, RegistryKey? root = null)
{
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    public const string ValueName = "ACAW";

    // Format de Windows : 12 octets, le premier dit l'état. Bit 0 à 1 = désactivé (03, ou 07 par une
    // stratégie) ; à 0 = activé (02, 06). Les 8 derniers octets datent la désactivation : zéro ici.
    private static readonly byte[] Enabled = [0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

    private readonly RegistryKey _root = root ?? Registry.CurrentUser;

    /// <summary>
    /// true seulement si Windows lancera vraiment ce programme : la commande pointe vers lui, et
    /// l'utilisateur ne l'a pas désactivé (ou Windows ne l'a jamais autorisé).
    /// </summary>
    public bool IsEnabled
    {
        get
        {
            using var run = _root.OpenSubKey(RunKey);
            if (run?.GetValue(ValueName) is not string command
                || !string.Equals(command.Trim().Trim('"'), executablePath, StringComparison.OrdinalIgnoreCase))
                return false;

            using var approved = _root.OpenSubKey(ApprovedKey);
            return approved?.GetValue(ValueName) is byte[] { Length: > 0 } state && (state[0] & 1) == 0;
        }
    }

    /// <summary>
    /// Active ou désactive le démarrage. Activer réécrit les deux valeurs : la commande (le programme a
    /// peut-être changé de dossier) et l'autorisation (c'est une demande explicite de l'utilisateur, qui
    /// prime sur une désactivation passée dans les Paramètres).
    /// </summary>
    public void Set(bool enabled)
    {
        using var run = _root.CreateSubKey(RunKey);
        using var approved = _root.CreateSubKey(ApprovedKey);
        if (enabled)
        {
            run.SetValue(ValueName, $"\"{executablePath}\"");
            approved.SetValue(ValueName, Enabled, RegistryValueKind.Binary);
        }
        else
        {
            run.DeleteValue(ValueName, throwOnMissingValue: false);
            approved.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
