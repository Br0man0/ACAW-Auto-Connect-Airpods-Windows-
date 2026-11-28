using System.ComponentModel;

namespace Acaw.Service;

/// <summary>
/// Les deux gestes de base, partagés par le test (W1-2), la console (W1-3) et plus tard le service (W1-5) :
/// prendre l'adaptateur (WinUSB, ouvert) et le rendre (pilote d'origine, radio de retour).
/// </summary>
internal static class AdapterTakeover
{
    private static readonly TimeSpan DriverSwitchTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Passe l'adaptateur sur WinUSB et l'ouvre. Null en cas d'échec (raison au journal) ; l'adaptateur reste alors
    /// noté comme pris, et l'appelant doit le rendre.
    /// </summary>
    public static WinUsbDevice? Take(string radio, CancellationToken cancel)
    {
        // Noté AVANT de changer le pilote : si tout s'arrête au milieu, --restore-native sait quoi rendre.
        ServiceState.RadioInstanceId = radio;
        ServiceState.RememberBound(radio);
        if (!AdapterDriver.IsOnWinUsb(radio) && (AdapterDriver.BindWinUsb(radio) || AdapterDriver.NeedsRestart(radio)))
            AdapterDriver.CyclePort(radio);
        if (!AdapterDriver.WaitFor(() => AdapterDriver.IsPresent(radio) && AdapterDriver.IsOnWinUsb(radio), DriverSwitchTimeout, cancel))
        {
            ServiceLog.Warn("pilote", "l'adaptateur n'est pas revenu sur WinUSB à temps");
            return null;
        }
        return OpenWithRetry(radio, cancel);
    }

    /// <summary>
    /// Les adaptateurs que le service a passés sur WinUSB et pas encore rendus, d'après l'état retenu dans le registre
    /// (valable même si le service vient de démarrer et n'a encore rien fait).
    /// </summary>
    public static List<string> HeldInstances()
    {
        var instances = ServiceState.BoundInstances.ToList();
        if (ServiceState.RadioInstanceId is { } stored && AdapterDriver.IsOnWinUsb(stored)
            && !instances.Contains(stored, StringComparer.OrdinalIgnoreCase))
            instances.Add(stored);
        return instances;
    }

    /// <summary>Rend leur pilote d'origine à tous les adaptateurs encore tenus. true si tous sont revenus.</summary>
    public static bool GiveBackAll()
    {
        bool allBack = true;
        foreach (string id in HeldInstances()) allBack &= GiveBackAndClean(id);
        return allBack;
    }

    /// <summary>
    /// Rend l'adaptateur à son pilote d'origine après le relais : attend que l'adaptateur virtuel soit retiré, oublie
    /// ses nœuds audio cachés (sinon le pilote d'origine numéroterait les siens « 2- »), puis <see cref="GiveBack"/>.
    /// </summary>
    public static bool GiveBackAndClean(string radio)
    {
        List<string> twins = DeviceCleanup.VirtualTwins(radio);
        AdapterDriver.WaitFor(() => !twins.Any(AdapterDriver.IsPresent), TimeSpan.FromSeconds(5));
        ServiceLog.Info("nœuds", $"{twins.Sum(DeviceCleanup.ForgetHiddenAudioNodes)} nœuds audio cachés de l'adaptateur virtuel oubliés");
        return GiveBack(radio);
    }

    /// <summary>
    /// Rend le pilote d'origine et attend que la radio Bluetooth soit de retour. true si c'est fait.
    /// </summary>
    public static bool GiveBack(string radio)
    {
        try
        {
            if (!AdapterDriver.IsPresent(radio))
            {
                ServiceLog.Warn("pilote", $"{radio} est débranché : rebranche-le, puis relance --restore-native");
                return false;
            }
            DateTime start = DateTime.UtcNow;
            if (AdapterDriver.IsOnWinUsb(radio) && (AdapterDriver.RestoreNative(radio) || AdapterDriver.NeedsRestart(radio)))
                AdapterDriver.CyclePort(radio);
            // Rendu = le pilote d'origine a redémarré et republié la radio Bluetooth, pas seulement le registre changé.
            bool back = AdapterDriver.WaitFor(() => AdapterDriver.IsPresent(radio) && !AdapterDriver.IsOnWinUsb(radio)
                                                    && AdapterDriver.IsRadioRunning(radio), DriverSwitchTimeout);
            if (back)
            {
                ServiceState.ForgetBound(radio);
                ServiceLog.Info("pilote", $"{radio} rendu, radio Bluetooth de retour en {(DateTime.UtcNow - start).TotalSeconds:0.0} s : "
                                          + $"pilote {AdapterDriver.Driver(radio)}");
            }
            else
            {
                ServiceLog.Warn("pilote", $"{radio} n'est pas revenu sur son pilote d'origine ({AdapterDriver.Driver(radio)}). "
                                          + "Secours : Gestionnaire de périphériques (guide 11).");
            }
            return back;
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            ServiceLog.Warn("pilote", $"retour au pilote d'origine impossible pour {radio} : {e.Message}");
            return false;
        }
    }

    // L'interface WinUSB apparaît un instant après le changement de pilote ; et Windows laisse parfois la pile de
    // l'ancien pilote à moitié retirée (erreur 433, vue sur un Intel AX211) : un rebranchement règle ça.
    private static WinUsbDevice? OpenWithRetry(string radio, CancellationToken cancel)
    {
        bool replugged = false;
        DateTime end = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (true)
        {
            try
            {
                return WinUsbDevice.Open(radio);
            }
            catch (Win32Exception e)
            {
                if (e.NativeErrorCode == 433 && !replugged)
                {
                    ServiceLog.Info("pilote", "pile de l'ancien pilote encore là (433) : rebranchement");
                    replugged = AdapterDriver.CyclePort(radio);
                }
                if (DateTime.UtcNow >= end || cancel.IsCancellationRequested)
                {
                    ServiceLog.Warn("pilote", $"ouverture WinUSB impossible : {e.Message} (erreur {e.NativeErrorCode})");
                    return null;
                }
                Thread.Sleep(500);
            }
        }
    }
}
