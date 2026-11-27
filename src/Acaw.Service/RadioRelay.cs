using System.Collections.Concurrent;
using System.Net.Sockets;
using Acaw.Bluetooth.Takeover;
using Acaw.Bluetooth.Usb;
using Acaw.Bluetooth.UsbIp;

namespace Acaw.Service;

/// <summary>
/// Le relais : chaque transfert USB demandé par Windows (par usbip-win2) est exécuté tel quel sur le vrai
/// adaptateur, et sa réponse renvoyée. Rien n'est modifié, sauf les descripteurs retouchés et les quelques
/// requêtes standard que WinUSB garde pour lui (voir <see cref="ControlRequest"/>).
/// </summary>
/// <remarks>
/// <para><b>Un fil par tuyau.</b> Comme un vrai contrôleur USB, les transferts d'un même point de terminaison se
/// terminent dans l'ordre où ils ont été demandés ; ceux de tuyaux différents avancent en parallèle (une lecture
/// d'événements reste en attente pendant qu'une commande part).</para>
/// <para><b>Annulation (UNLINK).</b> Windows annule parfois un transfert en attente (arrêt, réinitialisation). Une
/// réponse et une annulation s'excluent : un transfert déjà terminé répond normalement et l'annulation renvoie 0 ;
/// sinon l'annulation renvoie ECONNRESET et le transfert ne répondra jamais. Si une lecture interrompue avait
/// quand même reçu des données, elles sont gardées pour la lecture suivante du même tuyau : aucun événement HCI
/// n'est perdu.</para>
/// <para><b>Isochrone (appels).</b> Phase W1-4 : en W1-3, ces transferts sont refusés.</para>
/// </remarks>
internal sealed class RadioRelay : IDisposable
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly WinUsbDevice _device;
    private readonly CommandWatch? _commandWatch;
    private readonly byte[] _deviceDescriptor;
    private readonly byte[] _configurationDescriptor;
    private readonly string _serial;
    private readonly byte _serialIndex;
    private readonly byte _eventsPipe;
    private readonly Lock _writeLock = new();
    private readonly ConcurrentDictionary<uint, Urb> _live = new();
    private readonly ConcurrentDictionary<byte, BlockingCollection<Urb>> _queues = new();
    private readonly ConcurrentDictionary<byte, byte[]> _leftovers = new();
    private readonly List<Thread> _workers = [];
    private string? _endReason;
    private int _loggedControls;
    private bool _isoWarned;

    private sealed class Urb(UsbIpSubmit submit)
    {
        public UsbIpSubmit Submit { get; } = submit;
        public uint Seq => Submit.Header.SeqNum;
        public byte Pipe => Submit.Header.Endpoint == 0 ? (byte)0 : Submit.Header.EndpointAddress;
        public bool Running { get; set; }
        public bool Completed { get; set; }
        public bool Cancelled { get; set; }
    }

    public RadioRelay(TcpClient client, WinUsbDevice device, string serial, byte serialIndex, CommandWatch? commandWatch = null)
    {
        _client = client;
        _stream = client.GetStream();
        _device = device;
        _serial = serial;
        _serialIndex = serialIndex;
        _commandWatch = commandWatch;
        _deviceDescriptor = DescriptorPatch.PatchDevice(device.DeviceDescriptor, serialIndex);
        _configurationDescriptor = DescriptorPatch.PatchConfiguration(device.ConfigurationDescriptor);
        _eventsPipe = device.Configuration.FindHciPipes()?.Events ?? 0x81;
    }

    /// <summary>
    /// Relaie jusqu'à la fin de la connexion, la perte de l'adaptateur ou l'annulation. Rend la raison de l'arrêt.
    /// </summary>
    public string Run(CancellationToken cancel)
    {
        using CancellationTokenRegistration stop = cancel.Register(() => End("arrêt demandé"));
        var header = new byte[UsbIpFrames.HeaderSize];
        try
        {
            while (_endReason is null)
            {
                _stream.ReadExactly(header);
                UsbIpRequestHeader request = UsbIpFrames.ParseRequestHeader(header);
                if (request.Command == UsbIpCommand.Unlink)
                {
                    Unlink(request.SeqNum, request.UnlinkSeqNum);
                    continue;
                }
                var payload = new byte[request.PayloadLength];
                _stream.ReadExactly(payload);
                var urb = new Urb(UsbIpFrames.ParseSubmit(request, payload));
                if (urb.Submit.IsIsochronous)
                {
                    RefuseIso(urb);
                    continue;
                }
                _live[urb.Seq] = urb;
                // SET_INTERFACE passe tout de suite, avant les transferts isochrones que Windows envoie juste après :
                // derrière d'autres transferts de contrôle, il arriverait trop tard.
                if (urb.Pipe == 0 && ControlRequest.Parse(urb.Submit.Header.Setup).Classify(_serialIndex) == ControlKind.SetInterface)
                {
                    Execute(urb);
                    continue;
                }
                Queue(urb.Pipe).Add(urb);
            }
        }
        catch (Exception e) when (e is IOException or EndOfStreamException or ObjectDisposedException or SocketException or FormatException)
        {
            End(e is FormatException ? $"trame USB/IP invalide : {e.Message}" : "connexion USB/IP fermée");
        }
        Shutdown();
        return _endReason ?? "fin";
    }

    public void Dispose()
    {
        End("relais fermé");
        Shutdown();
    }

    // Arrête la connexion : la boucle de lecture se termine aussitôt. La première raison est gardée.
    private void End(string reason)
    {
        if (Interlocked.CompareExchange(ref _endReason, reason, null) is null) ServiceLog.Info("relais", $"arrêt : {reason}");
        _client.Dispose();
    }

    private void Shutdown()
    {
        foreach (BlockingCollection<Urb> queue in _queues.Values) queue.CompleteAdding();
        // Les lectures en attente ne finissent qu'en interrompant leurs tuyaux.
        foreach (UsbInterfaceSetting setting in _device.Configuration.Settings)
        {
            foreach (UsbEndpoint endpoint in setting.Endpoints) _device.Abort(endpoint.Address);
        }
        lock (_workers)
        {
            foreach (Thread worker in _workers) worker.Join(TimeSpan.FromSeconds(2));
            _workers.Clear();
        }
    }

    private BlockingCollection<Urb> Queue(byte pipe) => _queues.GetOrAdd(pipe, p =>
    {
        var queue = new BlockingCollection<Urb>();
        var worker = new Thread(() => Work(p, queue)) { IsBackground = true, Name = $"tuyau 0x{p:X2}" };
        lock (_workers) _workers.Add(worker);
        worker.Start();
        return queue;
    });

    private void Work(byte pipe, BlockingCollection<Urb> queue)
    {
        try
        {
            foreach (Urb urb in queue.GetConsumingEnumerable()) Execute(urb);
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException)
        {
            End($"tuyau 0x{pipe:X2} : {e.Message}");
        }
    }

    private void Execute(Urb urb)
    {
        lock (urb)
        {
            // Annulé avant d'avoir commencé, ou relais en train de s'arrêter : rien à exécuter (une nouvelle lecture
            // attendrait indéfiniment sur un tuyau qu'on vient d'interrompre).
            if (urb.Cancelled || _endReason is not null)
            {
                _live.TryRemove(urb.Seq, out _);
                return;
            }
            urb.Running = true;
        }
        (int status, int actual, byte[] inData) = urb.Pipe == 0 ? Control(urb) : urb.Submit.Header.In ? Read(urb) : Write(urb);
        lock (urb)
        {
            urb.Running = false;
            if (urb.Cancelled)
            {
                // L'annulation a déjà répondu. Des données lues malgré tout iront à la lecture suivante.
                if (urb.Pipe != 0 && urb.Submit.Header.In && inData.Length > 0) _leftovers[urb.Pipe] = inData;
            }
            else
            {
                urb.Completed = true;
                Send(UsbIpFrames.RetSubmit(urb.Seq, status, actual, inData));
            }
            // Retiré seulement après la réponse : une annulation qui arrive entre-temps attend ce verrou et répond
            // « déjà terminé » APRÈS la réponse, jamais avant.
            _live.TryRemove(urb.Seq, out _);
        }
    }

    private void Unlink(uint seq, uint victim)
    {
        if (!_live.TryGetValue(victim, out Urb? urb))
        {
            Send(UsbIpFrames.RetUnlink(seq, 0)); // déjà terminé (sa réponse est partie) ou inconnu
            return;
        }
        lock (urb)
        {
            if (urb.Completed)
            {
                Send(UsbIpFrames.RetUnlink(seq, 0));
                return;
            }
            urb.Cancelled = true;
            if (urb.Running && urb.Pipe != 0) _device.Abort(urb.Pipe);
            Send(UsbIpFrames.RetUnlink(seq, UsbIpFrames.ECONNRESET));
        }
    }

    private (int Status, int Actual, byte[] Data) Read(Urb urb)
    {
        byte pipe = urb.Pipe;
        var buffer = new byte[urb.Submit.Header.BufferLength];
        if (_leftovers.TryRemove(pipe, out byte[]? kept))
        {
            int n = Math.Min(kept.Length, buffer.Length);
            if (n < kept.Length) _leftovers[pipe] = kept[n..];
            return (0, n, kept[..n]);
        }
        int error = _device.Read(pipe, buffer, out uint got);
        if (error != 0) return (Fail(error, $"lecture 0x{pipe:X2}", urb), 0, []);
        if (pipe == _eventsPipe && got > 0) _commandWatch?.OnEvent();
        return (0, (int)got, buffer[..(int)got]);
    }

    private (int Status, int Actual, byte[] Data) Write(Urb urb)
    {
        int error = _device.Write(urb.Pipe, urb.Submit.OutData, out uint got);
        return error != 0 ? (Fail(error, $"écriture 0x{urb.Pipe:X2}", urb), 0, []) : (0, (int)got, []);
    }

    private (int Status, int Actual, byte[] Data) Control(Urb urb)
    {
        var request = ControlRequest.Parse(urb.Submit.Header.Setup);
        int wanted = urb.Submit.Header.BufferLength;
        ControlKind kind = request.Classify(_serialIndex);
        switch (kind)
        {
            case ControlKind.DeviceDescriptor:
                return Answer(_deviceDescriptor, wanted);
            case ControlKind.ConfigurationDescriptor:
                return Answer(_configurationDescriptor, wanted);
            case ControlKind.SerialDescriptor:
                return Answer(DescriptorPatch.StringDescriptor(_serial), wanted);
            case ControlKind.SetConfiguration:
                LogControl($"SET_CONFIGURATION {request.Value} (déjà fait par WinUSB)");
                return (0, 0, []);
            case ControlKind.SetInterface:
            {
                int error = _device.SetAlternate(request.InterfaceNumber, (byte)request.Value);
                ServiceLog.Info("relais", $"SET_INTERFACE {request.InterfaceNumber} réglage {request.Value} : "
                                          + (error == 0 ? "fait" : $"erreur Win32 {error}"));
                return (error == 0 ? 0 : UsbIpFrames.EPIPE, 0, []);
            }
            case ControlKind.ClearHalt:
                _device.ResetPipe((byte)request.Index);
                LogControl($"CLEAR_FEATURE(HALT) 0x{request.Index:X2}");
                return (0, 0, []);
        }
        if (kind == ControlKind.HciCommand) _commandWatch?.OnCommandSent();
        byte[] buffer = request.In ? new byte[wanted] : urb.Submit.OutData;
        int status = _device.Control(request, buffer, out uint got);
        if (status != 0)
        {
            // Un adaptateur sans aucune chaîne n'a pas non plus de liste de langues : la nôtre rend le numéro de série lisible.
            if (kind == ControlKind.LanguagesDescriptor) return Answer(DescriptorPatch.EnglishLanguages, wanted);
            return (Fail(status, $"contrôle {Convert.ToHexString(urb.Submit.Header.Setup)}", urb), 0, []);
        }
        if (kind != ControlKind.HciCommand)
            LogControl($"contrôle {Convert.ToHexString(urb.Submit.Header.Setup)} : {got} octets");
        return request.In ? (0, (int)got, buffer[..(int)got]) : (0, (int)got, []);
    }

    private static (int Status, int Actual, byte[] Data) Answer(byte[] data, int wanted)
    {
        byte[] part = data[..Math.Min(data.Length, wanted)];
        return (0, part.Length, part);
    }

    // Traduit une erreur WinUSB ; si l'adaptateur est parti, la session s'arrête.
    private int Fail(int error, string what, Urb urb)
    {
        if (WinUsbDevice.IsDeviceLost(error))
        {
            End($"adaptateur perdu ({what}, erreur Win32 {error})");
        }
        // 995 = transfert interrompu : normal pour une annulation ou à l'arrêt du relais (les lectures en attente
        // sont interrompues exprès), anormal sinon.
        else if (!(error == 995 && (urb.Cancelled || _endReason is not null)))
        {
            ServiceLog.Warn("relais", $"{what} : erreur Win32 {error}");
        }
        return UsbIpFrames.ErrnoFromWin32(error);
    }

    private void RefuseIso(Urb urb)
    {
        if (!_isoWarned)
        {
            _isoWarned = true;
            ServiceLog.Warn("relais", "transfert isochrone (audio d'un appel) refusé : pris en charge en phase W1-4");
        }
        Send(IsoReply.Build(urb.Seq, UsbIpFrames.EPROTO, (uint)urb.Submit.Header.StartFrame, urb.Submit.Packets,
                            urb.Submit.Header.In, default, results: null));
    }

    private void Send(byte[] frame)
    {
        try
        {
            lock (_writeLock) _stream.Write(frame);
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
            End("connexion USB/IP fermée pendant une réponse");
        }
    }

    // Les premiers transferts de contrôle (hors commandes HCI) vont au journal : c'est la mise en route par Windows.
    private void LogControl(string message)
    {
        if (Interlocked.Increment(ref _loggedControls) <= 60) ServiceLog.Info("relais", message);
    }
}
