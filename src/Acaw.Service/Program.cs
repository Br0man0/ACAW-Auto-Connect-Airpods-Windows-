using System.Security.Principal;
using System.ServiceProcess;
using Acaw.Service;
using Windows.Win32;

// acaw-service.exe : lancé sans argument par le gestionnaire de services, c'est le service « AcawService ».
// Sinon, commandes à lancer dans un terminal ADMINISTRATEUR (guide 11) :
//   --status          ce que le service voit : service, adaptateur, pilote, état retenu
//   --install         enregistre et démarre le service (depuis C:\Program Files\ACAW\service seulement)
//   --uninstall       arrête le service, le supprime et rend l'adaptateur à Windows
//   --retry           après un garde-fou : autorise de nouveau la prise en main et redémarre le service
//   --guard           (lancé par Windows au 3e plantage) rend l'adaptateur et note le garde-fou
//   --takeover-test   passe l'adaptateur sur WinUSB, lit ses descripteurs, puis rend le pilote d'origine (W1-2)
//   --console         relaie l'adaptateur jusqu'à Ctrl+C, puis le rend (W1-3, W1-4 ; service arrêté)
//   --restore-native  secours : rend leur pilote d'origine à tous les adaptateurs passés sur WinUSB

// La voix d'un appel est rythmée à la milliseconde (IsoOutClock) : on demande à Windows des minuteries de 1 ms au
// lieu de 15,6 ms. Depuis Windows 10 2004, ce réglage ne vaut que pour ce processus.
PInvoke.timeBeginPeriod(1);

if (args.Length == 0 && !Environment.UserInteractive)
{
    ServiceBase.Run(new AcawService());
    return 0;
}

string[] commands = ["--status", "--install", "--uninstall", "--retry", "--guard", "--takeover-test", "--console", "--restore-native"];
string command = args.FirstOrDefault() ?? "";
if (!commands.Contains(command))
{
    Console.WriteLine($"Utilisation : acaw-service {string.Join(" | ", commands)}");
    return 2;
}
if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
{
    Console.WriteLine("Cette commande exige un terminal ouvert en administrateur.");
    return 5;
}
if (command is "--takeover-test" or "--console" && ServiceSetup.Describe() is "en marche" or "en train de démarrer")
{
    Console.WriteLine("Le service ACAW tient déjà l'adaptateur : arrête-le d'abord (services.msc, ou --uninstall).");
    return 10;
}

using var cancel = new CancellationTokenSource();
// Ctrl+C ne coupe pas le programme au milieu d'un changement de pilote : il demande seulement d'arrêter,
// et la restitution du pilote d'origine se fait quand même.
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancel.Cancel();
    Console.WriteLine("Arrêt demandé : l'adaptateur va être rendu à son pilote d'origine…");
};

try
{
    return command switch
    {
        "--status" => Commands.Status(),
        "--install" => ServiceSetup.Install(),
        "--uninstall" => ServiceSetup.Uninstall(),
        "--retry" => ServiceSetup.Retry(),
        "--guard" => ServiceSetup.Guard(),
        "--takeover-test" => Commands.TakeoverTest(cancel.Token),
        "--console" => Commands.RunConsole(cancel.Token),
        _ => Commands.RestoreNative(),
    };
}
catch (Exception e)
{
    ServiceLog.Warn("service", $"erreur inattendue : {e}");
    return 1;
}
