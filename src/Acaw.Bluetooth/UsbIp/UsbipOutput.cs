using System.Globalization;
using System.Text.RegularExpressions;

namespace Acaw.Bluetooth.UsbIp;

/// <summary>Lecture de ce qu'affiche le programme <c>usbip.exe</c> d'usbip-win2.</summary>
public static partial class UsbipOutput
{
    /// <summary>
    /// Le port virtuel sur lequel usbip-win2 a branché l'adaptateur (« succesfully attached to port 1 » :
    /// usbip-win2 écrit lui-même « succesfully » avec un seul s). Null si absent. Ce port sert au débranchement
    /// (<c>usbip detach -p N</c>).
    /// </summary>
    public static int? AttachedPort(string output)
    {
        Match m = AttachedPortPattern().Match(output);
        return m.Success ? int.Parse(m.Groups["port"].ValueSpan, CultureInfo.InvariantCulture) : null;
    }

    [GeneratedRegex(@"attached to port (?<port>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex AttachedPortPattern();
}
