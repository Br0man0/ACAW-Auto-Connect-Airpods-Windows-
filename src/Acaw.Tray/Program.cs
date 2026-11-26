using Acaw.Core;
using Acaw.Core.Config;
using Acaw.Windows;

namespace Acaw.Tray;

internal static class Program
{
    // [STAThread] : WinForms exige que le thread de l'interface soit en mode « STA » (règle COM).
    [STAThread]
    private static void Main()
    {
        // Une seule icône à la fois : si ACAW tourne déjà (lancé au démarrage, puis à la main), on s'arrête.
        using var single = new Mutex(initiallyOwned: true, @"Local\ACAW", out bool first);
        // La langue avant la première ligne du journal ; le coordinateur la réappliquera à chaque relecture.
        Lang.Current = ReadLanguage();
        // Le temps depuis le démarrage de Windows distingue un lancement à l'ouverture de session d'un lancement à la main.
        var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
        string since = $"{(int)uptime.TotalHours} h {uptime.Minutes:D2} min {uptime.Seconds:D2} s";
        FileLog.Write(first
            ? Lang.Pick($"Lancement d'ACAW {AppInfo.Version} : {Application.ExecutablePath} (Windows démarré il y a {since})",
                        $"Starting ACAW {AppInfo.Version}: {Application.ExecutablePath} (Windows started {since} ago)")
            : Lang.Pick("ACAW tourne déjà : ce second lancement s'arrête", "ACAW is already running: this second launch stops"));
        if (!first) return;

        ApplicationConfiguration.Initialize();
        // « using » : en quittant, l'icône est libérée, donc l'hôte arrêté et le son coupé par ACAW rétabli.
        // Application.Run ne le fait pas tout seul.
        using var app = new TrayApp();
        Application.Run(app);
    }

    // config.json absent ou abîmé : la langue de Windows (le coordinateur signalera le problème ensuite).
    private static AppLanguage ReadLanguage()
    {
        try
        {
            return Lang.Resolve(ConfigStore.Load(ConfigStore.DefaultPath).Language);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return Lang.FromWindows();
        }
    }
}
