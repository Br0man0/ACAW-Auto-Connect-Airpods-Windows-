using System.Drawing.Drawing2D;
using Acaw.Core.Switching;

namespace Acaw.Tray;

/// <summary>Icônes dessinées au démarrage : un rond de couleur avec un « A », selon l'état des AirPods.</summary>
internal static class TrayIcons
{
    private static readonly Dictionary<Color, Icon> Cache = [];

    public static Icon For(SwitchState state, bool autoSwitch) => Draw(state switch
    {
        _ when !autoSwitch => Color.FromArgb(158, 158, 158),         // gris clair : bascule suspendue
        SwitchState.OnWindows => Color.FromArgb(46, 125, 50),        // vert : sur ce PC
        SwitchState.Switching or SwitchState.Releasing => Color.FromArgb(239, 108, 0), // orange : en cours
        _ => Color.FromArgb(84, 110, 122),                           // gris-bleu : ailleurs
    });

    public static string Describe(SwitchState state, bool autoSwitch)
    {
        string where = state switch
        {
            SwitchState.OnWindows => "AirPods sur ce PC",
            SwitchState.Switching => "Connexion des AirPods…",
            SwitchState.Releasing => "AirPods rendus…",
            SwitchState.Lost => "AirPods pris par un autre appareil",
            _ => "AirPods ailleurs ou libres",
        };
        return autoSwitch ? where : $"{where} · bascule suspendue";
    }

    private static Icon Draw(Color color)
    {
        if (Cache.TryGetValue(color, out var icon)) return icon;

        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var fill = new SolidBrush(color);
            g.FillEllipse(fill, 1, 1, 30, 30);
            using var font = new Font("Segoe UI", 15, FontStyle.Bold, GraphicsUnit.Pixel);
            TextRenderer.DrawText(g, "A", font, new Rectangle(0, 0, 32, 32), Color.White,
                                  TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        // Les icônes sont gardées pour toute la vie de l'application : quelques-unes seulement.
        icon = Icon.FromHandle(bitmap.GetHicon());
        Cache[color] = icon;
        return icon;
    }
}
