using System.Net.Sockets;
using Acaw.Bluetooth.Takeover;
using Acaw.Bluetooth.Usb;
using Acaw.Bluetooth.UsbIp;

namespace Acaw.Service;

/// <summary>
/// Une session de relais : l'adaptateur pris (ou repris s'il est déjà sur WinUSB), rendu à Windows par USB/IP,
/// relayé jusqu'à la fin, puis l'adaptateur virtuel débranché. Le pilote d'origine n'est <b>pas</b> rendu ici :
/// la console le rend après une session, le service seulement à l'arrêt ou par le garde-fou.
/// </summary>
internal static class RelaySession
{
    /// <summary>
    /// Déroule une session jusqu'à sa fin. Rend la raison de l'arrêt, et si Windows a retiré l'adaptateur pendant la
    /// session (Gestionnaire de périphériques, changement de pilote) ; lance une exception si la prise en main ou
    /// l'attache échoue.
    /// </summary>
    /// <param name="radio">L'adaptateur.</param>
    /// <param name="server">Le serveur USB/IP (le service garde le même d'une session à l'autre).</param>
    /// <param name="end">Annulé pour terminer la session : l'adaptateur virtuel est alors débranché proprement.</param>
    /// <param name="commandWatch">Surveillance du firmware après un réveil (service), ou null (console).</param>
    public static (string Reason, bool RemovedByWindows) RunOnce(string radio, UsbIpServer server, CancellationToken end,
                                                                 CommandWatch? commandWatch)
    {
        WinUsbDevice device = AdapterTakeover.Take(radio, end)
                              ?? throw new InvalidOperationException("prise en main de l'adaptateur impossible");
        RadioRelay? relay = null;
        string busId = server.NextBusId();
        int virtualPort = 0;
        bool detached = false;
        bool removed = false;
        var gate = new Lock();
        // Windows veut retirer l'adaptateur (Gestionnaire de périphériques…) : on lâche tout de suite la poignée.
        using RemovalWatch removal = RemovalWatch.Start(device.File, () =>
        {
            Volatile.Write(ref removed, true);
            lock (gate)
            {
                relay?.Dispose();
                device.Dispose();
            }
        });
        try
        {
            ServiceLog.Info("nœuds", $"{DeviceCleanup.ForgetHiddenAudioNodes(radio)} nœuds audio cachés de l'adaptateur d'origine oubliés");
            ServiceLog.Info("nœuds", $"{DeviceCleanup.CarryLeDeviceIds()} identifiants LE recopiés dans l'appairage");
            string serial = DescriptorPatch.VirtualSerial(radio);
            byte serialIndex = DescriptorPatch.ChooseSerialIndex(device.DeviceDescriptor, device.ConfigurationDescriptor);
            byte[] info = UsbIpFrames.DeviceInfo(busId, DescriptorPatch.PatchDevice(device.DeviceDescriptor, serialIndex),
                                                 device.ConfigurationDescriptor, device.Speed);
            (TcpClient client, virtualPort) = server.AttachAsync(busId, info, end).GetAwaiter().GetResult();
            ServiceLog.Info("session", $"adaptateur virtuel branché (port {virtualPort} d'usbip-win2, série {serial}, {device.Speed})");
            lock (gate) relay = new RadioRelay(client, device, serial, serialIndex, commandWatch);
            // À la fin demandée : débrancher d'abord (usbip-win2 retire l'adaptateur virtuel proprement), puis fermer.
            using CancellationTokenRegistration stop = end.Register(() =>
            {
                Volatile.Write(ref detached, true);
                UsbIpServer.Detach(virtualPort);
                relay.Dispose();
            });
            DateTime start = DateTime.UtcNow;
            string reason = relay.Run(CancellationToken.None);
            ServiceLog.Info("session", $"fin du relais après {(DateTime.UtcNow - start).TotalMinutes:0.0} min : {reason}");
            return (reason, Volatile.Read(ref removed));
        }
        finally
        {
            lock (gate)
            {
                relay?.Dispose();
                device.Dispose();
            }
            if (!Volatile.Read(ref detached)) UsbIpServer.Detach(virtualPort);
            // Sinon usbip-win2 retenterait seul de reconnecter l'adaptateur virtuel.
            server.StopReconnect(busId);
        }
    }

    /// <summary>
    /// Phase W1-3 / W1-4 : une session en console, jusqu'à Ctrl+C, puis le pilote d'origine rendu.
    /// Rend 0 si l'adaptateur est revenu sur son pilote d'origine.
    /// </summary>
    public static int RunConsole(CancellationToken cancel)
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
        using var server = new UsbIpServer();
        try
        {
            RunOnce(radio, server, cancel, commandWatch: null);
        }
        catch (Exception e) when (e is InvalidOperationException or OperationCanceledException or IOException or SocketException
                                       or System.ComponentModel.Win32Exception or FormatException)
        {
            ServiceLog.Warn("session", $"session interrompue : {e.Message}");
        }
        return AdapterTakeover.GiveBackAndClean(radio) ? 0 : 1;
    }
}
