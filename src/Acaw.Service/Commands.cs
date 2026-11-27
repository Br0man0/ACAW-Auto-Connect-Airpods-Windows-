using Acaw.Bluetooth.Usb;

namespace Acaw.Service;

/// <summary>Les commandes de la ligne de commande du service.</summary>
internal static class Commands
{
    /// <summary>Affiche l'adaptateur trouvé, son pilote et l'état retenu. Ne change rien.</summary>
    public static int Status()
    {
        string? radio = AdapterDriver.Find(out AdapterDriver.NotFoundReason reason);
        Console.WriteLine(radio is null ? $"Adaptateur : aucun ({Explain(reason)})" : $"Adaptateur : {radio}");
        if (radio is not null)
        {
            Console.WriteLine($"  présent : {(AdapterDriver.IsPresent(radio) ? "oui" : "non")}");
            Console.WriteLine($"  radio Bluetooth active : {(AdapterDriver.IsRadioRunning(radio) ? "oui" : "non")}");
            Console.WriteLine($"  pilote : {AdapterDriver.Driver(radio)}");
        }
        Console.WriteLine($"Adaptateur retenu : {ServiceState.RadioInstanceId ?? "aucun"}");
        Console.WriteLine($"Instances encore sur WinUSB : {(ServiceState.BoundInstances.Count == 0 ? "aucune" : string.Join(", ", ServiceState.BoundInstances))}");
        Console.WriteLine($"usbip-win2 : {(UsbIpServer.ClientInstalled ? "installé" : "absent")}");
        Console.WriteLine($"Journal : {ServiceLog.FilePath}");
        return 0;
    }

    /// <summary>
    /// Test de la phase W1-2 : prend l'adaptateur (WinUSB), lit ses descripteurs, puis le rend toujours à son
    /// pilote d'origine, même en cas d'erreur ou de Ctrl+C. Le Bluetooth du PC est coupé pendant ~30 s.
    /// </summary>
    public static int TakeoverTest(CancellationToken cancel)
    {
        string? radio = AdapterDriver.Find(out AdapterDriver.NotFoundReason reason);
        if (radio is null)
        {
            ServiceLog.Warn("test", $"aucun adaptateur à prendre : {Explain(reason)}");
            return 3;
        }
        ServiceLog.Info("test", $"adaptateur {radio}, pilote {AdapterDriver.Driver(radio)}");
        if (AdapterDriver.IsOnWinUsb(radio))
        {
            ServiceLog.Warn("test", "l'adaptateur est déjà sur WinUSB : lance d'abord --restore-native");
            return 4;
        }
        try
        {
            using WinUsbDevice? device = AdapterTakeover.Take(radio, cancel);
            if (device is null) return 7;
            Describe(radio, device);
            return 0;
        }
        finally
        {
            AdapterTakeover.GiveBack(radio);
        }
    }

    /// <summary>
    /// Phase W1-3 : relaie l'adaptateur jusqu'à Ctrl+C (ou une panne), puis le rend à son pilote d'origine.
    /// </summary>
    public static int RunConsole(CancellationToken cancel)
    {
        Console.WriteLine("Relais en marche. Ctrl+C pour arrêter et rendre l'adaptateur à son pilote d'origine.");
        return RelaySession.Run(cancel);
    }

    /// <summary>Secours : rend leur pilote d'origine à toutes les instances passées sur WinUSB.</summary>
    public static int RestoreNative()
    {
        var instances = ServiceState.BoundInstances.ToList();
        if (ServiceState.RadioInstanceId is { } stored && AdapterDriver.IsOnWinUsb(stored)
            && !instances.Contains(stored, StringComparer.OrdinalIgnoreCase))
            instances.Add(stored);
        if (instances.Count == 0)
        {
            Console.WriteLine("Aucun adaptateur à rendre : le service n'a rien passé sur WinUSB.");
            return 0;
        }
        bool allBack = true;
        foreach (string id in instances) allBack &= AdapterTakeover.GiveBack(id);
        return allBack ? 0 : 1;
    }

    // Écrit au journal ce que le relais doit connaître de l'adaptateur.
    private static void Describe(string radio, WinUsbDevice device)
    {
        ServiceLog.Info("test", $"descripteur de périphérique : {Convert.ToHexString(device.DeviceDescriptor)}");
        ServiceLog.Info("test", $"descripteur de configuration ({device.ConfigurationDescriptor.Length} octets) : "
                                + Convert.ToHexString(device.ConfigurationDescriptor));
        ServiceLog.Info("test", $"vitesse : {device.Speed}, bcdUSB {device.DeviceDescriptor[3]:X2}.{device.DeviceDescriptor[2]:X2}");
        UsbConfiguration configuration = device.Configuration;
        ServiceLog.Info("test", configuration.FindHciPipes() is { } hci
            ? $"tuyaux HCI : interface {hci.Interface}, événements 0x{hci.Events:X2}, ACL IN 0x{hci.AclIn:X2}, ACL OUT 0x{hci.AclOut:X2}"
            : "ALERTE : aucune interface HCI trouvée");
        foreach (UsbInterfaceSetting s in configuration.Settings.Where(s => s.Endpoints.Any(e => e.Type == UsbTransferType.Isochronous)))
        {
            var iso = configuration.IsoPipes(s.Number, s.Alternate);
            ServiceLog.Info("test", $"audio des appels : interface {s.Number} réglage {s.Alternate}, IN 0x{iso.In:X2} OUT 0x{iso.Out:X2}, {iso.MaxPacket} octets");
        }
        byte serialIndex = DescriptorPatch.ChooseSerialIndex(device.DeviceDescriptor, device.ConfigurationDescriptor);
        ServiceLog.Info("test", $"adaptateur virtuel prévu : numéro de série {DescriptorPatch.VirtualSerial(radio)} (index 0x{serialIndex:X2}), "
                                + $"réveil à distance annoncé : {((device.ConfigurationDescriptor[7] & 0x20) != 0 ? "oui (sera effacé)" : "non")}");
    }

    private static string Explain(AdapterDriver.NotFoundReason reason) => reason switch
    {
        AdapterDriver.NotFoundReason.NotUsb => "seulement des adaptateurs non USB (UART, SDIO), non pris en charge",
        AdapterDriver.NotFoundReason.Composite => "adaptateur intégré à un périphérique USB combiné, non pris en charge",
        _ => "aucune radio Bluetooth (Bluetooth coupé dans Windows ?)",
    };
}
