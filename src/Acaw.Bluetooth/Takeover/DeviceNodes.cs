using System.Globalization;
using System.Text.RegularExpressions;

namespace Acaw.Bluetooth.Takeover;

/// <summary>Identifiant d'un périphérique Bluetooth LE, tel que Windows l'écrit dans le nom de ses nœuds.</summary>
/// <param name="Source">Origine de l'identifiant fabricant (0x01 Bluetooth SIG, 0x02 USB).</param>
/// <param name="Address">Adresse Bluetooth, 12 chiffres hexadécimaux.</param>
public readonly record struct LeDeviceId(int Source, int Vid, int Pid, int Revision, string Address);

/// <summary>
/// Les règles sur les nœuds de périphériques Windows (le « Gestionnaire de périphériques ») qui entourent la
/// prise en main de l'adaptateur. La lecture du registre est faite par le service ; ici, seulement les choix.
/// </summary>
public static partial class DeviceNodes
{
    // Profils audio du Bluetooth classique : Windows crée un nœud par profil d'un appareil appairé,
    // BTHENUM\{<UUID 16 bits>-0000-1000-8000-00805f9b34fb}_…, et les endpoints audio en dessous.
    private static readonly string[] AudioProfiles =
    [
        "0000110a", "0000110b",             // A2DP source, puits
        "0000110c", "0000110e", "0000110f", // AVRCP cible, télécommande, contrôleur
        "0000111e", "0000111f",             // mains libres, passerelle mains libres
        "00001108", "00001112", "00001131", // casque, passerelle casque, casque HS
    ];

    /// <summary>true pour l'identifiant d'un adaptateur relié en USB (USB\VID_…).</summary>
    public static bool IsUsb(string instanceId) => instanceId.StartsWith(@"USB\VID_", StringComparison.OrdinalIgnoreCase);

    /// <summary>true pour une fonction d'un périphérique USB combiné (USB\VID_x&amp;PID_y&amp;MI_nn\…), non prise en charge.</summary>
    public static bool IsCompositeFunction(string instanceId) => instanceId.Contains("&MI_", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// L'identifiant d'instance d'un périphérique tiré du chemin d'une de ses interfaces :
    /// <c>\\?\USB#VID_8087&amp;PID_0026#5&amp;d58ed73&amp;0&amp;10#{guid}</c> → <c>USB\VID_8087&amp;PID_0026\5&amp;d58ed73&amp;0&amp;10</c>.
    /// Null si le chemin n'a pas cette forme.
    /// </summary>
    public static string? InstanceIdFromInterfacePath(string path)
    {
        const string prefix = @"\\?\";
        if (!path.StartsWith(prefix, StringComparison.Ordinal)) return null;
        int guid = path.LastIndexOf("#{", StringComparison.Ordinal);
        if (guid <= prefix.Length) return null;
        string id = path[prefix.Length..guid].Replace('#', '\\');
        return id.Count(c => c == '\\') == 2 ? id : null;
    }

    /// <summary>
    /// Le chemin de l'interface « périphérique USB » d'une instance, que le hub USB publie pour tout périphérique,
    /// quel que soit son pilote. C'est par lui que WinUSB ouvre l'adaptateur.
    /// </summary>
    public static string UsbDeviceInterfacePath(string instanceId) =>
        @"\\?\" + instanceId.Replace('\\', '#') + "#{a5dcbf10-6530-11d2-901f-00c04fb951ed}";

    /// <summary>true pour le nœud d'un profil audio Bluetooth classique.</summary>
    public static bool IsAudioProfile(string instanceId)
    {
        if (!instanceId.StartsWith(@"BTHENUM\{", StringComparison.OrdinalIgnoreCase) || instanceId.Length < 17) return false;
        ReadOnlySpan<char> uuid = instanceId.AsSpan(9, 8);
        foreach (string profile in AudioProfiles)
        {
            if (uuid.Equals(profile, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>
    /// Les nœuds à oublier sous <paramref name="rootId"/>, les enfants avant leur parent.
    /// </summary>
    /// <remarks>
    /// Quand l'adaptateur change de pilote, ses anciens nœuds deviennent cachés. S'ils restent, Windows numérote
    /// les nouveaux : « Casque (2- AirPods Pro) ». Avec <paramref name="audioOnly"/>, seuls les profils audio (et
    /// tout ce qui est en dessous) sont pris : retirer les nœuds d'appareil, HID ou LE peut défaire un appairage.
    /// Un enfant se reconnaît au début du dernier segment de son identifiant : le <c>ParentIdPrefix</c> du parent
    /// suivi de « &amp; ». Le service ne retire ensuite que les nœuds réellement cachés (jamais un présent).
    /// </remarks>
    /// <param name="rootId">Le nœud de départ.</param>
    /// <param name="allInstanceIds">Tous les identifiants d'instance connus de Windows.</param>
    /// <param name="parentIdPrefixOf">Le ParentIdPrefix d'un nœud, ou null s'il n'en a pas.</param>
    /// <param name="audioOnly">true pour ne prendre que les profils audio.</param>
    public static List<string> CollectSubtree(string rootId, IReadOnlyCollection<string> allInstanceIds,
                                              Func<string, string?> parentIdPrefixOf, bool audioOnly)
    {
        var found = new List<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { rootId };
        Collect(rootId, !audioOnly);
        return found;

        void Collect(string id, bool take)
        {
            if (parentIdPrefixOf(id) is not { Length: > 0 } prefix) return;
            foreach (string child in allInstanceIds)
            {
                ReadOnlySpan<char> last = child.AsSpan(child.LastIndexOf('\\') + 1);
                if (last.Length <= prefix.Length || last[prefix.Length] != '&'
                    || !last.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !visited.Add(child))
                    continue;
                bool takeChild = take || IsAudioProfile(child);
                Collect(child, takeChild);
                if (takeChild) found.Add(child);
            }
        }
    }

    /// <summary>
    /// Lit l'identifiant LE dans le nom d'un nœud BTHLEDevice
    /// (…_Dev_VID&amp;02045e_PID&amp;0b13_REV&amp;0509_0c352679a111).
    /// </summary>
    /// <remarks>
    /// Windows ne range cet identifiant dans l'appairage que pour l'adaptateur d'origine. Sans lui, la partie HID
    /// d'un périphérique LE (la manette Xbox) ne démarre pas sur l'adaptateur virtuel : le service l'y recopie.
    /// </remarks>
    public static bool TryParseLeDeviceId(string nodeName, out LeDeviceId id)
    {
        Match m = LeDeviceIdPattern().Match(nodeName);
        if (!m.Success)
        {
            id = default;
            return false;
        }
        id = new LeDeviceId(Hex(m, "source"), Hex(m, "vid"), Hex(m, "pid"), Hex(m, "rev"),
                            m.Groups["address"].Value.ToUpperInvariant());
        return true;

        static int Hex(Match m, string group) => int.Parse(m.Groups[group].ValueSpan, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    [GeneratedRegex(@"_Dev_VID&(?<source>[0-9a-f]{2})(?<vid>[0-9a-f]{4})_PID&(?<pid>[0-9a-f]{4})_REV&(?<rev>[0-9a-f]{4})_(?<address>[0-9a-f]{12})(\\|$)",
                    RegexOptions.IgnoreCase)]
    private static partial Regex LeDeviceIdPattern();
}
