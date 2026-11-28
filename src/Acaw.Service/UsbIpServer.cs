using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Acaw.Bluetooth.UsbIp;
using Windows.Win32;
using Windows.Win32.NetworkManagement.IpHelper;

namespace Acaw.Service;

/// <summary>
/// Le « serveur » USB/IP, sur 127.0.0.1 seulement, à un port choisi par Windows. usbip-win2 s'y connecte quand le
/// service lance <c>usbip attach</c>, et branche alors l'adaptateur virtuel.
/// </summary>
/// <remarks>
/// Sécurité : n'importe quel programme du PC peut se connecter à un port local. Un import n'est donc accepté que
/// <list type="bullet">
/// <item>pour l'identifiant de bus que le service est justement en train d'attacher (« armé ») ;</item>
/// <item>et si l'autre bout de la connexion est le <c>usbip.exe</c> lancé par le service, ou le pilote
/// d'usbip-win2 lui-même (processus « System », n° 4, qu'aucun programme ne peut être).</item>
/// </list>
/// Toute autre connexion est refusée. Chaque attache utilise un identifiant de bus neuf : usbip-win2 retente
/// seul une connexion perdue pendant un moment, et une ancienne tentative ne doit jamais tomber sur la nouvelle.
/// </remarks>
internal sealed class UsbIpServer : IDisposable
{
    private const int SystemProcess = 4;
    private const string ShutdownTask = @"\USBip Detach All On Reboot Or Shutdown";

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Lock _lock = new();
    private readonly Dictionary<string, Armed> _armed = [];
    private int _busIds;

    private sealed class Armed(byte[] deviceInfo)
    {
        public byte[] DeviceInfo { get; } = deviceInfo;
        public TaskCompletionSource<TcpClient> Connected { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int UsbipProcess { get; set; }
    }

    public UsbIpServer()
    {
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        ServiceLog.Info("usbip", $"écoute sur 127.0.0.1:{Port}");
        _ = AcceptLoopAsync();
    }

    /// <summary>Le port local choisi par Windows.</summary>
    public int Port { get; }

    /// <summary>Le programme usbip.exe d'usbip-win2.</summary>
    public static string UsbipExe { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "USBip", "usbip.exe");

    /// <summary>true si usbip-win2 est installé.</summary>
    public static bool ClientInstalled => File.Exists(UsbipExe);

    /// <summary>Un identifiant de bus neuf pour la prochaine attache (« 1-1 », « 1-2 »…).</summary>
    public string NextBusId() => $"1-{Interlocked.Increment(ref _busIds)}";

    /// <summary>
    /// Lance l'attache d'usbip-win2 et attend sa connexion. Rend la connexion et le port virtuel d'usbip-win2.
    /// Lance une exception en cas d'échec ou après 30 s.
    /// </summary>
    public async Task<(TcpClient Client, int VirtualPort)> AttachAsync(string busId, byte[] deviceInfo, CancellationToken cancel)
    {
        var armed = new Armed(deviceInfo);
        lock (_lock) _armed[busId] = armed;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            using Process process = StartUsbip("-t", Port.ToString(CultureInfo.InvariantCulture), "attach", "-r", "127.0.0.1", "-b", busId, "--once");
            lock (_lock) armed.UsbipProcess = process.Id;
            Task<string> output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            Task<string> errors = process.StandardError.ReadToEndAsync(timeout.Token);
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                process.Kill();
                throw;
            }
            string text = ((await output.ConfigureAwait(false)) + (await errors.ConfigureAwait(false))).Trim();
            ServiceLog.Info("usbip", $"attache {busId} : code {process.ExitCode} {text}");
            if (process.ExitCode != 0)
            {
                if (armed.Connected.Task.IsCompletedSuccessfully) armed.Connected.Task.Result.Dispose();
                throw new InvalidOperationException($"usbip attach {busId} a échoué ({process.ExitCode}) : {text}");
            }
            TcpClient client = await armed.Connected.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
            return (client, UsbipOutput.AttachedPort(text) ?? 0);
        }
        finally
        {
            lock (_lock)
            {
                if (_armed.TryGetValue(busId, out Armed? entry) && entry == armed) _armed.Remove(busId);
            }
            armed.Connected.TrySetCanceled(CancellationToken.None);
        }
    }

    /// <summary>
    /// Débranche l'adaptateur virtuel du port <paramref name="virtualPort"/> d'usbip-win2. Fermer la connexion ne
    /// suffit pas : usbip-win2 tenterait de la rétablir.
    /// </summary>
    public static void Detach(int virtualPort)
    {
        if (virtualPort > 0) Run($"débranchement du port {virtualPort}", "detach", "-p", virtualPort.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Arrête les nouvelles tentatives d'usbip-win2 pour <paramref name="busId"/> : après une attache, il retente
    /// seul une connexion perdue (à partir de 30 s, jusqu'à 20 fois), même pour un périphérique parti.
    /// </summary>
    public void StopReconnect(string busId) =>
        Run($"fin des reconnexions de {busId}", "-t", Port.ToString(CultureInfo.InvariantCulture), "attach", "-r", "127.0.0.1", "-b", busId, "--stop");

    /// <summary>
    /// Fait place nette avant une session du service : arrête toutes les tentatives de reconnexion d'usbip-win2
    /// (<c>attach --stop-all</c>) et débranche tous ses adaptateurs virtuels (<c>detach -a</c>).
    /// </summary>
    /// <remarks>
    /// Après un plantage du service, l'ancien adaptateur virtuel peut rester branché pendant qu'usbip-win2 retente
    /// de le reconnecter ; une nouvelle attache en mettrait un second, avec le même numéro de série. Options lues
    /// dans le code source d'usbip-win2 (<c>userspace/usbip/usbip.cpp</c>) : <c>detach -a</c> sans valeur débranche
    /// tous les ports. usbip-win2 ne sert qu'à ACAW sur ce PC : rien d'autre n'est débranché.
    /// </remarks>
    public static void CleanLeftovers()
    {
        Run("fin de toutes les tentatives de reconnexion", "attach", "--stop-all");
        Run("débranchement de tous les adaptateurs virtuels", "detach", "-a");
    }

    /// <summary>
    /// Active ou coupe la tâche planifiée d'usbip-win2 qui débranche tout à l'arrêt du PC : si l'arrêt est annulé,
    /// elle laisse des adaptateurs virtuels morts. Le service débranche lui-même les siens (phase W1-5).
    /// </summary>
    public static void SetShutdownTask(bool enabled) =>
        RunProgram(Path.Combine(Environment.SystemDirectory, "schtasks.exe"),
                   $"tâche « {ShutdownTask} » {(enabled ? "activée" : "désactivée")}",
                   ["/Change", "/TN", ShutdownTask, enabled ? "/ENABLE" : "/DISABLE"], OemEncoding());

    // schtasks.exe, lui, écrit dans la page de code de la console (850 en français) : lue en UTF-8,
    // « Opération » devenait « Op?ration » au journal (vu à l'installation du service, 2026-11-28).
    private static System.Text.Encoding OemEncoding()
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        return System.Text.Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
    }

    private static void Run(string what, params string[] args) => RunProgram(UsbipExe, what, args, System.Text.Encoding.UTF8);

    private static void RunProgram(string exe, string what, string[] args, System.Text.Encoding encoding)
    {
        try
        {
            using Process process = StartProcess(exe, args, encoding);
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> errors = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(15000)) process.Kill();
            ServiceLog.Info("usbip", $"{what} : code {(process.HasExited ? process.ExitCode : -1)} {(output.Result + errors.Result).Trim()}");
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            ServiceLog.Warn("usbip", $"{what} : impossible ({e.Message})");
        }
    }

    // usbip.exe écrit en UTF-8 ; lu avec la page de code de la console, « périphérique » devenait
    // « p├®riph├®rique » au journal (vu au test R2, 2026-11-27).
    private static Process StartUsbip(params string[] args) => StartProcess(UsbipExe, args, System.Text.Encoding.UTF8);

    private static Process StartProcess(string exe, string[] args, System.Text.Encoding encoding)
    {
        var info = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = encoding,
            StandardErrorEncoding = encoding,
        };
        foreach (string a in args) info.ArgumentList.Add(a);
        return Process.Start(info) ?? throw new InvalidOperationException($"{exe} n'a pas démarré");
    }

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                return;
            }
            _ = HandshakeAsync(client);
        }
    }

    // Demande d'import d'un identifiant armé, par usbip-win2 → réponse + description, et la connexion passe au relais.
    private async Task HandshakeAsync(TcpClient client)
    {
        client.NoDelay = true;
        NetworkStream stream = client.GetStream();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var request = new byte[UsbIpFrames.ImportRequestSize];
            await stream.ReadExactlyAsync(request.AsMemory(0, 8), timeout.Token).ConfigureAwait(false);
            if (BinaryPrimitives.ReadUInt16BigEndian(request.AsSpan(2)) != UsbIpFrames.OpRequestImport)
            {
                ServiceLog.Warn("usbip", $"opération refusée : 0x{BinaryPrimitives.ReadUInt16BigEndian(request.AsSpan(2)):X4}");
                client.Dispose();
                return;
            }
            await stream.ReadExactlyAsync(request.AsMemory(8), timeout.Token).ConfigureAwait(false);
            string busId = UsbIpFrames.ParseImportRequest(request) ?? "";
            int peer = PeerProcess(client);
            Armed? entry;
            bool accepted;
            lock (_lock)
            {
                accepted = _armed.TryGetValue(busId, out entry)
                           && (peer == SystemProcess || peer == entry.UsbipProcess)
                           && _armed.Remove(busId);
            }
            if (!accepted)
            {
                ServiceLog.Warn("usbip", entry is null
                    ? $"import de « {busId} » refusé : aucune attache en cours"
                    : $"import de « {busId} » refusé : demandé par le processus {peer}, pas par usbip-win2");
                await stream.WriteAsync(UsbIpFrames.ImportReply(null), timeout.Token).ConfigureAwait(false);
                client.Dispose();
                return;
            }
            await stream.WriteAsync(UsbIpFrames.ImportReply(entry!.DeviceInfo), timeout.Token).ConfigureAwait(false);
            ServiceLog.Info("usbip", $"import de {busId} accepté (processus {peer})");
            if (!entry.Connected.TrySetResult(client)) client.Dispose();
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or SocketException or ObjectDisposedException)
        {
            ServiceLog.Warn("usbip", $"poignée de main échouée : {e.Message}");
            client.Dispose();
        }
    }

    // Le processus à l'autre bout d'une connexion locale (-1 si introuvable), d'après la table TCP de Windows.
    private int PeerProcess(TcpClient client)
    {
        if (client.Client.RemoteEndPoint is not IPEndPoint remote) return -1;
        const uint AfInet = 2;
        uint size = 0;
        _ = PInvoke.GetExtendedTcpTable(default, ref size, false, AfInet, TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_CONNECTIONS, 0);
        var table = new byte[size + 4096];
        size = (uint)table.Length;
        if (PInvoke.GetExtendedTcpTable(table, ref size, false, AfInet, TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_CONNECTIONS, 0) != 0)
            return -1;
        // MIB_TCPTABLE_OWNER_PID : nombre de lignes, puis 6 entiers par ligne : état, adresse et port locaux,
        // adresse et port distants, processus. Les ports sont dans les 16 bits bas, octets réseau (big-endian).
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(table);
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> row = table.AsSpan(4 + i * 24, 24);
            int localPort = BinaryPrimitives.ReadUInt16BigEndian(row[8..]);
            int remotePort = BinaryPrimitives.ReadUInt16BigEndian(row[16..]);
            // Vu depuis le client : son port local est notre port distant, et son port distant est notre port d'écoute.
            if (localPort == remote.Port && remotePort == Port) return (int)BinaryPrimitives.ReadUInt32LittleEndian(row[20..]);
        }
        return -1;
    }
}
