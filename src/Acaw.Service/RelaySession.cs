using System.Net.Sockets;
using Acaw.Bluetooth.Takeover;
using Acaw.Bluetooth.Usb;
using Acaw.Bluetooth.UsbIp;

namespace Acaw.Service;

/// <summary>
/// Une session complète : prendre l'adaptateur, le rendre à Windows par USB/IP, relayer jusqu'à la fin, puis tout
/// défaire et rendre le pilote d'origine. Phase W1-3 : lancée par <c>--console</c> ; en W1-5, par le service.
/// </summary>
internal static class RelaySession
{
    /// <summary>
    /// Déroule une session. Rend 0 si l'adaptateur a été rendu à son pilote d'origine à la fin.
    /// </summary>
    public static int Run(CancellationToken cancel)
    {
        if (!UsbIpServer.ClientInstalled)
        {
            ServiceLog.Warn("session", $"usbip-win2 n'est pas installé ({UsbIpServer.UsbipExe}) : voir le guide 11");
            return 8;
        }
        string? radio = AdapterDriver.Find(out AdapterDriver.NotFoundReason reason);
        if (radio is null)
        {
            ServiceLog.Warn("session", $"aucun adaptateur à prendre ({reason})");
            return 3;
        }
        ServiceLog.Info("session", $"adaptateur {radio}, pilote {AdapterDriver.Driver(radio)}");

        WinUsbDevice? device = null;
        UsbIpServer? server = null;
        string? busId = null;
        int virtualPort = 0;
        try
        {
            device = AdapterTakeover.Take(radio, cancel);
            if (device is null) return 6;
            ServiceLog.Info("nœuds", $"{DeviceCleanup.ForgetHiddenAudioNodes(radio)} nœuds audio cachés de l'adaptateur d'origine oubliés");
            ServiceLog.Info("nœuds", $"{DeviceCleanup.CarryLeDeviceIds()} identifiants LE recopiés dans l'appairage");

            string serial = DescriptorPatch.VirtualSerial(radio);
            byte serialIndex = DescriptorPatch.ChooseSerialIndex(device.DeviceDescriptor, device.ConfigurationDescriptor);
            server = new UsbIpServer();
            busId = server.NextBusId();
            byte[] info = UsbIpFrames.DeviceInfo(busId, DescriptorPatch.PatchDevice(device.DeviceDescriptor, serialIndex),
                                                 device.ConfigurationDescriptor, device.Speed);
            (TcpClient client, virtualPort) = server.AttachAsync(busId, info, cancel).GetAwaiter().GetResult();
            ServiceLog.Info("session", $"adaptateur virtuel branché (port {virtualPort} d'usbip-win2, série {serial}, {device.Speed})");
            using var relay = new RadioRelay(client, device, serial, serialIndex);
            DateTime start = DateTime.UtcNow;
            string end = relay.Run(cancel);
            ServiceLog.Info("session", $"fin du relais après {(DateTime.UtcNow - start).TotalMinutes:0.0} min : {end}");
            return 0;
        }
        catch (Exception e) when (e is InvalidOperationException or OperationCanceledException or IOException or SocketException
                                       or System.ComponentModel.Win32Exception or FormatException)
        {
            ServiceLog.Warn("session", $"session interrompue : {e.Message}");
            return 1;
        }
        finally
        {
            UsbIpServer.Detach(virtualPort);
            if (busId is not null) server?.StopReconnect(busId);
            server?.Dispose();
            device?.Dispose();
            // Windows retire l'adaptateur virtuel un instant après le débranchement ; ses nœuds audio deviennent cachés.
            Thread.Sleep(2000);
            int forgotten = DeviceCleanup.VirtualTwins(radio).Sum(DeviceCleanup.ForgetHiddenAudioNodes);
            ServiceLog.Info("nœuds", $"{forgotten} nœuds audio cachés de l'adaptateur virtuel oubliés");
            AdapterTakeover.GiveBack(radio);
        }
    }
}
