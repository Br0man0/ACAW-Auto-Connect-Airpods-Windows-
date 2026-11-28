using System.ServiceProcess;

namespace Acaw.Service;

/// <summary>
/// Le service Windows « AcawService » (LocalSystem, démarrage automatique) : il fait tourner le
/// <see cref="Supervisor"/> et lui transmet les événements de Windows (arrêt, extinction, veille, réveil).
/// </summary>
internal sealed class AcawService : ServiceBase
{
    public const string Name = "AcawService";

    private Supervisor? _supervisor;

    public AcawService()
    {
        ServiceName = Name;
        CanStop = true;
        CanShutdown = true;
        CanHandlePowerEvent = true;
    }

    protected override void OnStart(string[] args)
    {
        ServiceLog.Info("service", $"démarrage du service, version {typeof(AcawService).Assembly.GetName().Version}");
        _supervisor = new Supervisor();
        _supervisor.Start();
    }

    /// <summary>Arrêt demandé (services.msc, désinstallation) : l'adaptateur est rendu à son pilote d'origine.</summary>
    protected override void OnStop()
    {
        ServiceLog.Info("service", "arrêt du service : l'adaptateur est rendu à Windows");
        RequestAdditionalTime(60_000);
        _supervisor?.Stop(giveBack: true);
    }

    /// <summary>
    /// Arrêt ou redémarrage de Windows : l'adaptateur virtuel est débranché proprement, mais l'adaptateur reste sur
    /// WinUSB (le changer de pilote à chaque démarrage coûterait ~10 s) ; le service le reprend au démarrage suivant.
    /// </summary>
    protected override void OnShutdown()
    {
        ServiceLog.Info("service", "arrêt de Windows : adaptateur virtuel débranché, adaptateur gardé sur WinUSB");
        _supervisor?.Stop(giveBack: false);
    }

    protected override bool OnPowerEvent(PowerBroadcastStatus powerStatus)
    {
        switch (powerStatus)
        {
            case PowerBroadcastStatus.Suspend:
                _supervisor?.OnSuspend();
                break;
            case PowerBroadcastStatus.ResumeSuspend or PowerBroadcastStatus.ResumeAutomatic:
                _supervisor?.OnResume();
                break;
        }
        return true;
    }
}
