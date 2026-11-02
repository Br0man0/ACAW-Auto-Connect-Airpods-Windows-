namespace Acaw.Windows;

/// <summary>
/// Journal horodaté de l'application résidente : %LOCALAPPDATA%\ACAW\acaw.log.
/// Au-delà de 1 Mo, il est renommé en acaw.old.log et on repart d'un fichier vide.
/// Appelable depuis n'importe quel thread.
/// </summary>
public static class FileLog
{
    private const long MaxSize = 1024 * 1024;
    private static readonly Lock Gate = new();

    public static string Path { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ACAW", "acaw.log");

    public static void Write(string message)
    {
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}{Environment.NewLine}";
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                var file = new FileInfo(Path);
                if (file.Exists && file.Length > MaxSize)
                    File.Move(Path, System.IO.Path.ChangeExtension(Path, ".old.log"), overwrite: true);
                File.AppendAllText(Path, line);
            }
            catch (Exception)
            {
                // Un journal qui ne s'écrit pas (disque plein, droits, fichier verrouillé…) ne doit jamais
                // faire tomber l'application : on perd la ligne, c'est tout.
            }
        }
    }
}
