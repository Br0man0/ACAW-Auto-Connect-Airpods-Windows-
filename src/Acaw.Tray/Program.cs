namespace Acaw.Tray;

internal static class Program
{
    // [STAThread] : WinForms exige que le thread de l'interface soit en mode « STA » (règle COM).
    [STAThread]
    private static void Main()
    {
        // Une seule icône à la fois : si ACAW tourne déjà (lancé au démarrage, puis à la main), on s'arrête.
        using var single = new Mutex(initiallyOwned: true, @"Local\ACAW", out bool first);
        if (!first) return;

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApp());
    }
}
