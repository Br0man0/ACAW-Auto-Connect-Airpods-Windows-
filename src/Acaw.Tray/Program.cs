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
        // Le temps depuis le démarrage de Windows distingue un lancement à l'ouverture de session d'un lancement à la main.
        var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
        FileLog.Write(first
            ? $"Lancement de {Application.ExecutablePath} (Windows démarré il y a {(int)uptime.TotalHours} h {uptime.Minutes:D2} min {uptime.Seconds:D2} s)"
            : "ACAW tourne déjà : ce second lancement s'arrête");
        if (!first) return;

        ApplicationConfiguration.Initialize();
        // « using » : en quittant, l'icône est libérée, donc l'hôte arrêté et le son coupé par ACAW rétabli.
        // Application.Run ne le fait pas tout seul.
        using var app = new TrayApp();
        Application.Run(app);
    }
}
