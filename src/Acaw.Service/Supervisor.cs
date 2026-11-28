using System.Net.Sockets;
using Acaw.Bluetooth.Takeover;

namespace Acaw.Service;

/// <summary>
/// Le cœur du service Windows : garde l'adaptateur relayé tant que le service tourne, relance une session qui
/// s'arrête, et rend l'adaptateur à Windows si ça échoue trop souvent.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>Entre deux sessions</b>, l'adaptateur reste sur WinUSB : seule la session (l'adaptateur virtuel) est
/// refaite, en 2 s.</item>
/// <item><b>Garde-fou</b> (<see cref="TakeoverGuard"/>) : 3 sessions ratées en 10 min (moins de 60 s chacune, hors
/// réveil) → pilote d'origine rendu, et plus de reprise tant que <c>--retry</c> n'est pas lancé.</item>
/// <item><b>Veille</b> : au réveil, le firmware est surveillé 20 s (<see cref="CommandWatch"/>) ; s'il est perdu,
/// la session est refaite. Un arrêt de session dans la fenêtre du réveil n'est pas un échec.</item>
/// <item><b>Arrêt du service</b> : pilote d'origine rendu. <b>Arrêt de Windows</b> : adaptateur virtuel débranché
/// proprement, mais l'adaptateur reste sur WinUSB ; le service le reprend au démarrage suivant.</item>
/// </list>
/// </remarks>
internal sealed class Supervisor
{
    private static readonly TimeSpan BetweenSessions = TimeSpan.FromSeconds(2);

    private readonly CancellationTokenSource _stop = new();
    private readonly TakeoverGuard _guard = new(TimeProvider.System);
    private readonly CommandWatch _commandWatch = new(TimeProvider.System);
    private readonly Lock _sessionLock = new();
    private CancellationTokenSource? _session;    // annulé pour terminer la session en cours (réveil, arrêt)
    private Thread? _thread;
    private string? _radio;
    private DateTimeOffset? _resumedAt;
    private volatile bool _firmwareReplug;

    public void Start()
    {
        _thread = new Thread(Supervise) { IsBackground = true, Name = "superviseur" };
        _thread.Start();
    }

    /// <summary>
    /// Arrête le relais (adaptateur virtuel débranché proprement). Avec <paramref name="giveBack"/>, rend aussi le
    /// pilote d'origine.
    /// </summary>
    public void Stop(bool giveBack)
    {
        _stop.Cancel();
        lock (_sessionLock) _session?.Cancel();
        _thread?.Join(TimeSpan.FromSeconds(20));
        if (giveBack && _radio is { } radio && AdapterDriver.IsOnWinUsb(radio)) AdapterTakeover.GiveBackAndClean(radio);
    }

    /// <summary>Le PC va se mettre en veille.</summary>
    public void OnSuspend() => ServiceLog.Info("service", "mise en veille");

    /// <summary>Le PC se réveille : surveillance du firmware pendant 20 s.</summary>
    public void OnResume()
    {
        _resumedAt = DateTimeOffset.UtcNow;
        _commandWatch.Arm();
        ServiceLog.Info("service", $"réveil : l'adaptateur est surveillé {CommandWatch.ResumeWatch.TotalSeconds:0} s");
        ThreadPool.QueueUserWorkItem(_ =>
        {
            DateTime until = DateTime.UtcNow + CommandWatch.ResumeWatch;
            while (DateTime.UtcNow < until && !_stop.IsCancellationRequested)
            {
                if (_commandWatch.FirmwareLost())
                {
                    ServiceLog.Info("service", "après le réveil : commande sans réponse depuis 3 s, firmware perdu : session refaite");
                    _firmwareReplug = true;
                    lock (_sessionLock) _session?.Cancel();
                    return;
                }
                Thread.Sleep(250);
            }
        });
    }

    private void Supervise()
    {
        CancellationToken stop = _stop.Token;
        while (!stop.IsCancellationRequested)
        {
            if (ServiceState.GuardTripped)
            {
                ServiceLog.Warn("service", "garde-fou déclenché : l'adaptateur reste sur son pilote d'origine (acaw-service --retry pour reprendre)");
                stop.WaitHandle.WaitOne();
                break;
            }
            if (!UsbIpServer.ClientInstalled)
            {
                ServiceLog.Warn("service", "usbip-win2 n'est pas installé : nouvel essai dans 30 s");
                if (stop.WaitHandle.WaitOne(TimeSpan.FromSeconds(30))) break;
                continue;
            }
            if (AdapterDriver.Find(out AdapterDriver.NotFoundReason reason) is not { } radio)
            {
                ServiceLog.Warn("service", $"aucun adaptateur à prendre ({reason}) : nouvel essai dans 10 s");
                if (stop.WaitHandle.WaitOne(TimeSpan.FromSeconds(10))) break;
                continue;
            }
            _radio = radio;
            RunOneSession(radio, stop);
            if (stop.WaitHandle.WaitOne(BetweenSessions)) break;
        }
        ServiceLog.Info("service", "superviseur arrêté");
    }

    private void RunOneSession(string radio, CancellationToken stop)
    {
        using var session = CancellationTokenSource.CreateLinkedTokenSource(stop);
        lock (_sessionLock) _session = session;
        DateTimeOffset start = DateTimeOffset.UtcNow;
        bool failedToStart = false;
        _firmwareReplug = false;
        try
        {
            // L'installation d'usbip-win2 (même une réinstallation) réactive sa tâche d'arrêt : on la recoupe à chaque fois.
            UsbIpServer.SetShutdownTask(false);
            // Un adaptateur virtuel laissé par un plantage ne doit pas cohabiter avec le nouveau.
            UsbIpServer.CleanLeftovers();
            using var server = new UsbIpServer();
            RelaySession.RunOnce(radio, server, session.Token, _commandWatch);
        }
        catch (Exception e) when (e is InvalidOperationException or OperationCanceledException or IOException or SocketException
                                       or System.ComponentModel.Win32Exception or FormatException or UnauthorizedAccessException)
        {
            if (stop.IsCancellationRequested) return;
            ServiceLog.Warn("service", $"session impossible : {e.Message}");
            failedToStart = true;
        }
        finally
        {
            lock (_sessionLock) _session = null;
        }
        if (stop.IsCancellationRequested) return;

        bool resume = _firmwareReplug || ResumeWindow.Explains(_resumedAt, DateTimeOffset.UtcNow);
        bool giveBack = failedToStart && !resume ? _guard.RecordFailure() : _guard.RecordSessionEnd(start, resume);
        if (resume) ServiceLog.Info("service", "session arrêtée par la veille : pas compté comme un échec");
        else if (_guard.RecentFailures > 0) ServiceLog.Warn("service", $"échecs récents : {_guard.RecentFailures} sur {TakeoverGuard.MaxFailures} en 10 min");
        if (giveBack)
        {
            ServiceLog.Warn("service", $"{TakeoverGuard.MaxFailures} échecs en 10 min : l'adaptateur est rendu à Windows (garde-fou)");
            ServiceState.GuardTripped = true;
            AdapterTakeover.GiveBackAndClean(radio);
        }
    }
}
