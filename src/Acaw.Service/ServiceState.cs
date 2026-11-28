using Microsoft.Win32;

namespace Acaw.Service;

/// <summary>
/// Ce que le service doit retenir d'un lancement à l'autre, dans HKLM\SOFTWARE\ACAW\Service (écriture réservée
/// aux administrateurs) : quel adaptateur il a pris, et toutes les instances qu'il a passées sur WinUSB.
/// </summary>
/// <remarks>
/// Écrit <b>avant</b> chaque changement de pilote : si le service s'arrête brutalement au milieu, la commande de
/// secours (<c>--restore-native</c>) sait toujours quel adaptateur rendre. Une fois sur WinUSB, l'adaptateur
/// n'apparaît plus comme radio Bluetooth : sans cette trace, on ne le retrouverait plus.
/// </remarks>
internal static class ServiceState
{
    private const string KeyPath = @"SOFTWARE\ACAW\Service";

    /// <summary>L'adaptateur que le service utilise (identifiant d'instance), ou null.</summary>
    public static string? RadioInstanceId
    {
        get => Registry.LocalMachine.OpenSubKey(KeyPath)?.GetValue("RadioInstanceId") as string;
        set
        {
            using RegistryKey key = Registry.LocalMachine.CreateSubKey(KeyPath);
            if (value is null) key.DeleteValue("RadioInstanceId", throwOnMissingValue: false);
            else key.SetValue("RadioInstanceId", value);
        }
    }

    /// <summary>Toutes les instances que le service a passées sur WinUSB et pas encore rendues.</summary>
    public static IReadOnlyList<string> BoundInstances =>
        Registry.LocalMachine.OpenSubKey(KeyPath)?.GetValue("BoundInstances") as string[] ?? [];

    public static void RememberBound(string instanceId)
    {
        if (BoundInstances.Any(b => b.Equals(instanceId, StringComparison.OrdinalIgnoreCase))) return;
        Save([.. BoundInstances, instanceId]);
    }

    public static void ForgetBound(string instanceId) =>
        Save([.. BoundInstances.Where(b => !b.Equals(instanceId, StringComparison.OrdinalIgnoreCase))]);

    /// <summary>
    /// true quand le garde-fou a rendu l'adaptateur (3 échecs en 10 min) : le service ne le reprend plus tant que
    /// Romsy ne le demande pas (<c>acaw-service --retry</c>). Retenu même après un redémarrage.
    /// </summary>
    public static bool GuardTripped
    {
        get => Registry.LocalMachine.OpenSubKey(KeyPath)?.GetValue("GuardTripped") is 1;
        set
        {
            using RegistryKey key = Registry.LocalMachine.CreateSubKey(KeyPath);
            key.SetValue("GuardTripped", value ? 1 : 0, RegistryValueKind.DWord);
        }
    }

    private static void Save(string[] instances)
    {
        using RegistryKey key = Registry.LocalMachine.CreateSubKey(KeyPath);
        if (instances.Length == 0) key.DeleteValue("BoundInstances", throwOnMissingValue: false);
        else key.SetValue("BoundInstances", instances, RegistryValueKind.MultiString);
    }
}
