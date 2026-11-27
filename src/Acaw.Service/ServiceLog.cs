using System.Security.AccessControl;
using System.Security.Principal;

namespace Acaw.Service;

/// <summary>
/// Journal du service : C:\ProgramData\ACAW\service.log, recopié à la console quand il y en a une.
/// Au-delà de 2 Mo, il devient service.old.log. Appelable depuis n'importe quel thread.
/// </summary>
/// <remarks>
/// ProgramData n'est pas redirigé par le conteneur MSIX de l'app Claude : ce journal-là est lisible tel quel.
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

    private static bool FolderUsable()
    {
        if (_folderChecked) return _folderUsable;
        _folderChecked = true;
        var dir = new DirectoryInfo(Folder);
        if (!dir.Exists)
        {
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            const InheritanceFlags all = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                                                            FileSystemRights.FullControl, all, PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                                                            FileSystemRights.FullControl, all, PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
                                                            FileSystemRights.ReadAndExecute, all, PropagationFlags.None, AccessControlType.Allow));
            dir.Create(security);
            dir.Refresh();
        }
        _folderUsable = !dir.Attributes.HasFlag(FileAttributes.ReparsePoint);
        if (!_folderUsable) Console.WriteLine($"Journal désactivé : {Folder} est un lien, pas un dossier.");
        return _folderUsable;
    }
}
