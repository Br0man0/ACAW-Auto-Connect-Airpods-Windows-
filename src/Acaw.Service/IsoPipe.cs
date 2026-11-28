using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Acaw.Bluetooth.UsbIp;
using Windows.Win32;
using Windows.Win32.Devices.Usb;

namespace Acaw.Service;

/// <summary>Un transfert isochrone demandé par Windows (un morceau de l'audio d'un appel).</summary>
internal sealed unsafe class IsoTransfer(UsbIpSubmit submit)
{
    public UsbIpSubmit Submit { get; } = submit;
    public uint Seq => Submit.Header.SeqNum;
    public bool In => Submit.Header.In;
    public bool Running { get; set; }
    public bool Completed { get; set; }
    public bool Cancelled { get; set; }
    public NativeOverlapped* Overlapped { get; set; }
}

/// <summary>
/// Un tuyau isochrone (l'audio SCO d'un appel), ouvert pour le réglage alternatif actuel de son interface.
/// </summary>
/// <remarks>
/// <para><b>Plusieurs transferts en vol</b>, comme sur un vrai contrôleur : chacun part vers WinUSB dès qu'il
/// arrive, et un seul fil les termine dans l'ordre. Une réception s'enchaîne à la précédente tant qu'une est en
/// vol ; un envoi part toujours à la prochaine trame possible (<see cref="IsoStream"/>).</para>
/// <para><b>Mémoire</b> : WinUSB exige un tampon enregistré une fois pour toutes, découpé ici en
/// <see cref="Slots"/> cases de <see cref="SlotSize"/> octets, une par transfert en vol. Ce tampon appartient
/// au réglage actuel : le tuyau doit être fermé avant tout SET_INTERFACE.</para>
/// <para><b>Rythme</b> : un envoi n'est déclaré terminé qu'une fois ses trames passées
/// (<see cref="IsoOutClock"/>), car Windows règle son débit sur ces fins de transfert.</para>
/// Comportements relevés par WPods, réécrits ici.
/// </remarks>
internal sealed unsafe class IsoPipe
{
    private const int Slots = 64;
    private const int SlotSize = 4096;
    private const int MaxPackets = 256;

    private readonly SafeHandle _interface;
    private readonly byte _pipe;
    private readonly int _bytesPerInterval;
    private readonly byte* _buffer;
    private readonly USBD_ISO_PACKET_DESCRIPTOR* _descriptors;
    private readonly NativeOverlapped*[] _overlapped = new NativeOverlapped*[Slots];
    private readonly ManualResetEvent[] _events = new ManualResetEvent[Slots];
    private readonly void* _registration;
    private readonly BlockingCollection<(IsoTransfer Transfer, int Slot, long EndFrame)> _pending = new();
    private readonly Action<IsoTransfer, int, byte[]?, IsoPacketResult[]?> _done;
    private readonly IsoOutClock _clock = new();
    private readonly Thread _completer;
    private readonly IsoSlots _slots = new(Slots);           // quelles cases sont en vol (sous _slotLock)
    private readonly Lock _slotLock = new();
    private readonly SemaphoreSlim _free = new(Slots, Slots); // nombre de cases libres : on attend s'il n'y en a plus
    private int _inFlight; // transferts confiés à WinUSB et pas encore terminés
    private volatile bool _closing;
    private long _ok;
    private long _failed;
    private long _empty;

    /// <param name="done">Appelé à la fin de chaque transfert : état, données reçues, résultat par paquet (null si raté).</param>
    public IsoPipe(WinUsbDevice device, byte pipe, Action<IsoTransfer, int, byte[]?, IsoPacketResult[]?> done)
    {
        var info = device.IsoPipe(pipe) ?? throw new InvalidOperationException($"tuyau 0x{pipe:X2} sans bande passante dans le réglage actuel");
        _interface = info.Interface;
        InterfaceNumber = info.InterfaceNumber;
        _bytesPerInterval = info.BytesPerInterval;
        _pipe = pipe;
        _done = done;
        _buffer = (byte*)NativeMemory.AllocZeroed(Slots * SlotSize);
        _descriptors = (USBD_ISO_PACKET_DESCRIPTOR*)NativeMemory.AllocZeroed((nuint)(Slots * MaxPackets * sizeof(USBD_ISO_PACKET_DESCRIPTOR)));
        for (int i = 0; i < Slots; i++)
        {
            _events[i] = new ManualResetEvent(false);
            _overlapped[i] = (NativeOverlapped*)NativeMemory.AllocZeroed((nuint)sizeof(NativeOverlapped));
            _overlapped[i]->EventHandle = _events[i].SafeWaitHandle.DangerousGetHandle();
        }
        if (!PInvoke.WinUsb_RegisterIsochBuffer(_interface, pipe, new Span<byte>(_buffer, Slots * SlotSize), out _registration))
        {
            int error = Marshal.GetLastPInvokeError();
            Free();
            throw new Win32Exception(error, $"tampon isochrone du tuyau 0x{pipe:X2} refusé");
        }
        ServiceLog.Info("appels", $"tuyau isochrone 0x{pipe:X2} ouvert, {_bytesPerInterval} octets par trame");
        _completer = new Thread(Complete) { IsBackground = true, Name = $"iso 0x{pipe:X2}", Priority = ThreadPriority.Highest };
        _completer.Start();
    }

    /// <summary>Le numéro de l'interface du tuyau (pour le fermer avant un SET_INTERFACE).</summary>
    public byte InterfaceNumber { get; }

    /// <summary>La trame USB actuelle, comme usbip-win2 0.9.8.1 la compte : millisecondes depuis le démarrage.</summary>
    public static long NowFrame()
    {
        PInvoke.QueryInterruptTimePrecise(out ulong time);
        return (long)(time / 10_000);
    }

    /// <summary>Envoie un transfert à WinUSB. Un seul fil appelle (le relais, sous son verrou isochrone).</summary>
    public void Submit(IsoTransfer transfer)
    {
        int packets = transfer.Submit.Packets.Length;
        int length = transfer.In ? packets * _bytesPerInterval : transfer.Submit.OutData.Length;
        if (length > SlotSize || packets > MaxPackets)
        {
            ServiceLog.Warn("appels", $"transfert isochrone trop grand sur 0x{_pipe:X2} ({length} octets, {packets} paquets)");
            _done(transfer, UsbIpFrames.EPROTO, null, null);
            return;
        }
        if (!transfer.In && length == 0)
        {
            // Envoi vide (paquets de longueur 0 : rien à jouer). WinUSB refuse une écriture de 0 octet ; il n'y a
            // rien à envoyer : il est seulement déclaré terminé quand ses trames sont passées.
            _pending.Add((transfer, -1, _clock.EndFrame((uint)transfer.Submit.Header.StartFrame, packets, NowFrame())));
            return;
        }
        // Une case libre, jamais celle d'un transfert encore en vol (revue W1-6, point 2) : si toutes sont prises, on
        // attend qu'un transfert se termine (le fil de fin libère sa case).
        if (!_free.Wait(TimeSpan.FromSeconds(2)))
        {
            ServiceLog.Warn("appels", $"tuyau isochrone 0x{_pipe:X2} : aucune case libre depuis 2 s, transfert refusé");
            _done(transfer, UsbIpFrames.EPROTO, null, null);
            return;
        }
        int slot;
        lock (_slotLock) slot = _slots.TryTake() ?? throw new InvalidOperationException("case isochrone introuvable malgré le compteur");
        NativeOverlapped* overlapped = _overlapped[slot];
        _events[slot].Reset();
        overlapped->InternalLow = 0;
        overlapped->InternalHigh = 0;
        overlapped->OffsetLow = 0;
        overlapped->OffsetHigh = 0;
        transfer.Overlapped = overlapped;
        uint offset = (uint)(slot * SlotSize);
        long endFrame = 0;
        if (!transfer.In)
        {
            transfer.Submit.OutData.AsSpan().CopyTo(new Span<byte>(_buffer + offset, length));
            endFrame = _clock.EndFrame((uint)transfer.Submit.Header.StartFrame, packets, NowFrame());
        }
        // Enchaîner seulement une réception, et seulement si un transfert est encore en vol (voir IsoStream).
        bool continueStream = IsoStream.ShouldContinue(transfer.In, Volatile.Read(ref _inFlight));
        int error = Start(transfer, slot, offset, length, continueStream);
        if (error != 0 && continueStream)
        {
            // Le flux s'est tari entre-temps : on le relance à la prochaine trame possible.
            error = Start(transfer, slot, offset, length, continueStream: false);
        }
        if (error != 0)
        {
            ServiceLog.Warn("appels", $"transfert isochrone refusé sur 0x{_pipe:X2} : erreur Win32 {error}");
            ReleaseSlot(slot);
            _done(transfer, UsbIpFrames.EPROTO, null, null);
            return;
        }
        transfer.Running = true;
        Interlocked.Increment(ref _inFlight);
        _pending.Add((transfer, slot, endFrame));
    }

    /// <summary>Annule un transfert en vol (Windows l'a retiré). Sans effet s'il n'est pas parti.</summary>
    public static void Cancel(WinUsbDevice device, IsoTransfer transfer)
    {
        if (transfer.Running && transfer.Overlapped != null) PInvoke.CancelIoEx(device.File, transfer.Overlapped);
    }

    /// <summary>Interrompt ce qui est en vol, attend les fins, puis libère le tampon.</summary>
    public void Close()
    {
        _closing = true;
        PInvoke.WinUsb_AbortPipe(_interface, _pipe);
        _pending.CompleteAdding();
        if (_completer.Join(TimeSpan.FromSeconds(2)))
        {
            PInvoke.WinUsb_UnregisterIsochBuffer(_registration);
            Free();
        }
        // Sinon la mémoire reste réservée : WinUSB pourrait encore y écrire.
        ServiceLog.Info("appels", $"tuyau isochrone 0x{_pipe:X2} fermé : {_ok} transferts réussis, {_failed} ratés, {_empty} envois vides");
    }

    private int Start(IsoTransfer transfer, int slot, uint offset, int length, bool continueStream)
    {
        bool ok = transfer.In
            ? PInvoke.WinUsb_ReadIsochPipeAsap(_registration, offset, (uint)length, continueStream, (uint)transfer.Submit.Packets.Length,
                                               _descriptors + slot * MaxPackets, transfer.Overlapped)
            : PInvoke.WinUsb_WriteIsochPipeAsap(_registration, offset, (uint)length, continueStream, transfer.Overlapped);
        int error = ok ? 0 : Marshal.GetLastPInvokeError();
        return error == 997 /* ERROR_IO_PENDING : parti, en cours */ ? 0 : error;
    }

    private void ReleaseSlot(int slot)
    {
        lock (_slotLock) _slots.Release(slot);
        _free.Release();
    }

    // Le fil de fin. Si l'adaptateur a été fermé pendant qu'il attendait encore (fermeture plus longue que 2 s),
    // il s'arrête sans faire tomber le service (revue W1-6, point 7).
    private void Complete()
    {
        try
        {
            CompleteAll();
        }
        catch (ObjectDisposedException)
        {
            ServiceLog.Warn("appels", $"tuyau isochrone 0x{_pipe:X2} : adaptateur fermé pendant l'attente d'un transfert");
        }
    }

    private void CompleteAll()
    {
        foreach ((IsoTransfer transfer, int slot, long endFrame) in _pending.GetConsumingEnumerable())
        {
            IsoPacket[] packets = transfer.Submit.Packets;
            if (slot < 0)
            {
                _empty++;
                WaitForFrame(endFrame);
                _done(transfer, 0, null, [.. packets.Select(p => new IsoPacketResult(0, 0, true))]);
                continue;
            }
            bool ok = PInvoke.WinUsb_GetOverlappedResult(_interface, in *transfer.Overlapped, out _, true);
            int error = ok ? 0 : Marshal.GetLastPInvokeError();
            transfer.Running = false;
            Interlocked.Decrement(ref _inFlight);
            if (!ok)
            {
                ReleaseSlot(slot);
                if (error != 995 && ++_failed <= 5)
                {
                    // Le détail des premiers échecs, pour comprendre lesquels WinUSB refuse.
                    string lengths = string.Join(",", packets.Take(12).Select(p => p.Length));
                    ServiceLog.Warn("appels", $"transfert isochrone raté sur 0x{_pipe:X2} : erreur Win32 {error} "
                                              + $"({packets.Length} paquets, {(transfer.In ? packets.Length * _bytesPerInterval : transfer.Submit.OutData.Length)} octets, "
                                              + $"longueurs {lengths}{(packets.Length > 12 ? "…" : "")})");
                }
                // Un envoi raté attend quand même ses trames : sinon Windows enverrait le suivant trop tôt.
                if (!transfer.In && error != 995) WaitForFrame(endFrame);
                _done(transfer, error == 995 ? UsbIpFrames.ECONNRESET : UsbIpFrames.EPROTO, null, null);
                continue;
            }
            _ok++;
            if (!transfer.In)
            {
                ReleaseSlot(slot); // WinUSB a fini avec la case ; l'attente des trames ne la bloque pas
                WaitForFrame(endFrame);
                _done(transfer, 0, null, [.. packets.Select(p => new IsoPacketResult(0, (int)p.Length, true))]);
                continue;
            }
            var data = new byte[packets.Length * _bytesPerInterval];
            new ReadOnlySpan<byte>(_buffer + slot * SlotSize, data.Length).CopyTo(data);
            var results = new IsoPacketResult[packets.Length];
            USBD_ISO_PACKET_DESCRIPTOR* d = _descriptors + slot * MaxPackets;
            for (int i = 0; i < results.Length; i++) results[i] = new IsoPacketResult((int)d[i].Offset, (int)d[i].Length, d[i].Status == 0);
            ReleaseSlot(slot); // données et résultats recopiés : la case peut resservir
            _done(transfer, 0, data, results);
        }
    }

    // Attend que la trame soit passée ; écourté à la fermeture.
    private void WaitForFrame(long frame)
    {
        while (!_closing)
        {
            long ms = frame - NowFrame();
            if (ms <= 0) return;
            Thread.Sleep((int)Math.Min(ms, 50));
        }
    }

    private void Free()
    {
        for (int i = 0; i < Slots; i++)
        {
            if (_overlapped[i] != null) NativeMemory.Free(_overlapped[i]);
            _overlapped[i] = null;
            _events[i]?.Dispose();
        }
        NativeMemory.Free(_buffer);
        NativeMemory.Free(_descriptors);
    }
}
