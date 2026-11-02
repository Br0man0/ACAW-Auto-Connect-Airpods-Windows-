using Microsoft.Win32;

namespace Acaw.Tray;

/// <summary>
/// « Démarrer avec Windows » : une valeur dans la clé Run de l'utilisateur (HKCU), que Windows lit à
/// l'ouverture de session. Aucun droit administrateur nécessaire, et rien n'est installé ailleurs.
/// </summary>
internal static class Startup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ACAW";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string command
                && string.Equals(command.Trim('"'), Application.ExecutablePath, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName, $"\"{Application.ExecutablePath}\"");
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
