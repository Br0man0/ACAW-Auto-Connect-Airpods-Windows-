using Acaw.Core;
using System.Drawing.Drawing2D;
using Acaw.Core.Switching;

namespace Acaw.Tray;

/// <summary>
/// Icônes dessinées au démarrage : un rond de couleur avec un « A », selon l'état des AirPods. En mode manuel
/// (bascule automatique décochée), la couleur reste celle de l'état et une barre blanche s'ajoute en bas.
/// </summary>
internal static class TrayIcons
{
    private static readonly Dictionary<(Color Color, bool Manual), Icon> Cache = [];

    private static readonly Color Green = Color.FromArgb(46, 125, 50);       // vert : sur ce PC
    private static readonly Color Orange = Color.FromArgb(239, 108, 0);      // orange : en cours
    private static readonly Color BlueGrey = Color.FromArgb(84, 110, 122);   // gris-bleu : ailleurs
    private static readonly Color LightGrey = Color.FromArgb(158, 158, 158); // gris clair : ACAW ne peut pas fonctionner

    public static Icon For(SwitchState state, bool autoSwitch) => Draw(state switch
    {
        SwitchState.OnWindows => Green,
        SwitchState.Switching or SwitchState.Releasing => Orange,
        _ => BlueGrey,
    }, manual: !autoSwitch);

    /// <summary>ACAW ne peut pas fonctionner (non configuré, AirPods introuvables…).</summary>
    public static Icon Unavailable => Draw(LightGrey, manual: false);

    public static string Describe(SwitchState state, bool autoSwitch)
    {
        string where = state switch
        {
            SwitchState.OnWindows => Lang.Pick("AirPods sur ce PC", "AirPods on this PC"),
            SwitchState.Switching => Lang.Pick("Connexion des AirPods…", "Connecting the AirPods…"),
            SwitchState.Releasing => Lang.Pick("AirPods rendus…", "Giving the AirPods back…"),
            SwitchState.Lost => Lang.Pick("AirPods pris par un autre appareil", "AirPods taken by another device"),
            _ => Lang.Pick("AirPods ailleurs ou libres", "AirPods elsewhere or idle"),
        };
        return autoSwitch ? where : $"{where} · {Lang.Pick("bascule manuelle", "manual switching")}";
    }

    private static Icon Draw(Color color, bool manual)
    {
        if (Cache.TryGetValue((color, manual), out var icon)) return icon;

        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var fill = new SolidBrush(color);
            g.FillEllipse(fill, 1, 1, 30, 30);
            using var font = new Font("Segoe UI", 15, FontStyle.Bold, GraphicsUnit.Pixel);
            // En manuel, le « A » remonte un peu pour laisser la place à la barre.
            var letter = manual ? new Rectangle(0, -3, 32, 32) : new Rectangle(0, 0, 32, 32);
            TextRenderer.DrawText(g, "A", font, letter, Color.White,
                                  TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if (manual)
            {
                // Barre blanche : « mode manuel, ACAW ne bouge pas les AirPods tout seul ».
                using var bar = new SolidBrush(Color.White);
                g.FillRectangle(bar, 9, 23, 14, 3);
            }
        }
        // Les icônes sont gardées pour toute la vie de l'application : quelques-unes seulement.
        icon = Icon.FromHandle(bitmap.GetHicon());
        Cache[(color, manual)] = icon;
        return icon;
    }
}
