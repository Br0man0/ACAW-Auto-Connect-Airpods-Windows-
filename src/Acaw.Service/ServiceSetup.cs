using System.Diagnostics;
using System.ServiceProcess;

namespace Acaw.Service;

/// <summary>
/// Installation, désinstallation et secours du service Windows, avec l'outil <c>sc.exe</c> de Windows.
/// </summary>
internal static class ServiceSetup
{
    private static string Exe => Environment.ProcessPath ?? throw new InvalidOperationException("chemin du programme inconnu");

    /// <summary>
    /// true si <paramref name="exePath"/> est dans <paramref name="programFiles"/> : un programme lancé en LocalSystem
    /// ne doit jamais venir d'un dossier où un utilisateur ordinaire peut écrire (il pourrait le remplacer et
    /// obtenir les droits du système).
    /// </summary>
    public static bool IsSafeLocation(string exePath, string programFiles)
    {
        string full = Path.GetFullPath(exePath);
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(programFiles)) + Path.DirectorySeparatorChar;
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>true si le service est installé.</summary>
    public static bool Installed => ServiceController.GetServices().Any(s => s.ServiceName == AcawService.Name);

    /// <summary>L'état du service, en clair.</summary>
    public static string Describe()
    {
        if (!Installed) return "non installé";
        using var sc = new ServiceController(AcawService.Name);
        return sc.Status switch
        {
            ServiceControllerStatus.Running => "en marche",
            ServiceControllerStatus.Stopped => "arrêté",
            ServiceControllerStatus.StartPending => "en train de démarrer",
            ServiceControllerStatus.StopPending => "en train de s'arrêter",
            _ => sc.Status.ToString(),
        };
    }

    /// <summary>
    /// Enregistre le service (démarrage automatique, LocalSystem), avec le garde-fou du gestionnaire de services :
    /// redémarrage après 1 s puis 5 s, et au 3e plantage en 10 min, <c>--guard</c> rend l'adaptateur. Puis le démarre.
    /// </summary>
    public static int Install()
    {
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!IsSafeLocation(Exe, programFiles))
        {
            Console.WriteLine($"Refusé : le service doit être lancé depuis {programFiles} (ici : {Exe}). Utilise build\\install-service.ps1.");
            return 9;
        }
        string bin = $"\"{Exe}\"";
        int result = Sc(Installed ? "config" : "create", AcawService.Name, "binPath=", bin, "start=", "auto", "obj=", "LocalSystem",
                        "DisplayName=", "ACAW (adaptateur Bluetooth)");
        if (result != 0) return result;
        Sc("description", AcawService.Name, "Relaie l'adaptateur Bluetooth pour ACAW (canal AAP des AirPods). Arrêter le service rend l'adaptateur à Windows.");
        if (Sc("failure", AcawService.Name, "reset=", "600", "actions=", "restart/1000/restart/5000/run/0", "command=", $"{bin} --guard") != 0
            || Sc("failureflag", AcawService.Name, "1") != 0)
            return 1;
        ServiceState.GuardTripped = false;
        UsbIpServer.SetShutdownTask(false);
        return Start();
    }

    /// <summary>Arrête le service (qui rend l'adaptateur), le supprime, et vérifie que l'adaptateur est rendu.</summary>
    public static int Uninstall()
    {
        int stopped = Stop();
        if (Installed && Sc("delete", AcawService.Name) != 0) return 1;
        UsbIpServer.SetShutdownTask(true);
        int restored = Commands.RestoreNative();
        return stopped == 0 && restored == 0 ? 0 : 1;
    }

    /// <summary>
    /// Lancé par le gestionnaire de services au 3e plantage en 10 min : rend l'adaptateur et note le garde-fou.
    /// Le service reste arrêté.
    /// </summary>
    public static int Guard()
    {
        ServiceLog.Warn("service", "le service plante sans cesse : l'adaptateur est rendu à Windows (garde-fou)");
        ServiceState.GuardTripped = true;
        return Commands.RestoreNative();
    }

    /// <summary>Après un garde-fou : autorise de nouveau la prise en main et redémarre le service.</summary>
    public static int Retry()
    {
        ServiceState.GuardTripped = false;
        Console.WriteLine("Garde-fou levé.");
        return Installed ? Restart() : 0;
    }

    private static int Start()
    {
        try
        {
            using var sc = new ServiceController(AcawService.Name);
            if (sc.Status != ServiceControllerStatus.Running) sc.Start();
            sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
            Console.WriteLine("Service démarré.");
            return 0;
        }
        catch (Exception e) when (e is InvalidOperationException or System.ServiceProcess.TimeoutException)
        {
            Console.WriteLine($"Démarrage du service impossible : {e.Message}");
            return 1;
        }
    }

    private static int Restart() => Stop() == 0 ? Start() : 1;

    private static int Stop()
    {
        if (!Installed) return 0;
        try
        {
            using var sc = new ServiceController(AcawService.Name);
            if (sc.Status is not (ServiceControllerStatus.Stopped or ServiceControllerStatus.StopPending)) sc.Stop();
            sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(90));
            return 0;
        }
        catch (Exception e) when (e is InvalidOperationException or System.ServiceProcess.TimeoutException)
        {
            Console.WriteLine($"Arrêt du service impossible : {e.Message}");
            return 1;
        }
    }

    private static int Sc(params string[] args)
    {
        var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "sc.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        };
        foreach (string a in args) info.ArgumentList.Add(a);
        using Process process = Process.Start(info) ?? throw new InvalidOperationException("sc.exe n'a pas démarré");
        string output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        if (process.ExitCode != 0) Console.WriteLine($"sc {args[0]} : {output}");
        return process.ExitCode;
    }
}
