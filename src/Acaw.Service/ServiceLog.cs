using System.Security.AccessControl;
using System.Security.Principal;

namespace Acaw.Service;

/// <summary>
/// Journal du service : C:\ProgramData\ACAW\service.log, recopié à la console quand il y en a une.
/// Au-delà de 2 Mo, il devient service.old.log. Appelable depuis n'importe quel thread.
/// </summary>
/// <remarks>
/// ProgramData n'est pas redirigé pour les applications empaquetées (MSIX) : ce journal-là est lisible tel quel.
/// Sécurité : le service écrit en LocalSystem. Le dossier est créé avec des droits restreints (écriture pour
/// SYSTEM et les administrateurs seulement), et si quelqu'un l'a remplacé par un lien (« junction »), le
/// service n'y écrit pas : sinon un programme ordinaire pourrait lui faire écraser un fichier système.
/// </remarks>
internal static class ServiceLog
{
    private const long MaxSize = 2 * 1024 * 1024;
    private static readonly Lock Gate = new();
    private static bool _folderChecked;
    private static bool _folderUsable;

    public static string Folder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ACAW");

    public static string FilePath { get; } = Path.Combine(Folder, "service.log");

    public static void Info(string area, string message) => Write("INFO ", area, message);

    public static void Warn(string area, string message) => Write("ALERTE", area, message);

    private static void Write(string level, string area, string message)
    {
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} [{area}] {message}";
        lock (Gate)
        {
            if (!Console.IsOutputRedirected || Environment.UserInteractive) Console.WriteLine(line);
            try
            {
                if (!FolderUsable()) return;
                var file = new FileInfo(FilePath);
                if (file.Exists && file.Length > MaxSize)
                    File.Move(FilePath, Path.ChangeExtension(FilePath, ".old.log"), overwrite: true);
                File.AppendAllText(FilePath, line + Environment.NewLine);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Un journal qui ne s'écrit pas ne doit jamais arrêter la prise en main ni la restitution.
            }
        }
    }

    /// <summary>
    /// true si le dossier appartient au système ou aux administrateurs. Un dossier créé par un compte ordinaire (avant
    /// l'installation, par exemple) pourrait contenir des pièges : le service n'y écrit pas (revue W1-6, point 3).
    /// </summary>
    internal static bool IsTrustedOwner(SecurityIdentifier? owner) =>
        owner is not null && (owner.IsWellKnown(WellKnownSidType.LocalSystemSid) || owner.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid));

    private static bool FolderUsable()
    {
        if (_folderChecked) return _folderUsable;
        _folderChecked = true;
        _folderUsable = false;
        try
        {
            var dir = new DirectoryInfo(Folder);
            if (!dir.Exists) dir.Create(SafeSecurity(withOwner: true));
            dir.Refresh();
            if (dir.Attributes.HasFlag(FileAttributes.ReparsePoint))
                return Disable($"{Folder} est un lien, pas un dossier");
            if (!IsTrustedOwner(dir.GetAccessControl().GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier))
                return Disable($"{Folder} a été créé par un autre compte que le système ou les administrateurs");
            // Droits remis à neuf à chaque démarrage : écriture pour SYSTEM et les administrateurs seulement.
            dir.SetAccessControl(SafeSecurity(withOwner: false));
            var file = new FileInfo(FilePath);
            if (file.Exists && file.Attributes.HasFlag(FileAttributes.ReparsePoint))
                return Disable($"{FilePath} est un lien, pas un fichier");
            _folderUsable = true;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return Disable($"dossier du journal inutilisable ({e.Message})");
        }
    }

    private static bool Disable(string why)
    {
        Console.WriteLine($"Journal désactivé : {why}.");
        return false;
    }

    // Le propriétaire n'est fixé qu'à la création : le changer ensuite exigerait un privilège qu'un administrateur
    // en console n'active pas.
    private static DirectorySecurity SafeSecurity(bool withOwner)
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        if (withOwner) security.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
        const InheritanceFlags all = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                                                        FileSystemRights.FullControl, all, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                                                        FileSystemRights.FullControl, all, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
                                                        FileSystemRights.ReadAndExecute, all, PropagationFlags.None, AccessControlType.Allow));
        return security;
    }
}
