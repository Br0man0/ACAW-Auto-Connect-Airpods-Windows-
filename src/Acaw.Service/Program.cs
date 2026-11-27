using System.Security.Principal;
using Acaw.Service;

// acaw-service.exe : commandes à lancer dans un terminal ADMINISTRATEUR (guide 11).
//   --status          ce que le service voit : adaptateur, pilote, état retenu
//   --takeover-test   passe l'adaptateur sur WinUSB, lit ses descripteurs, puis rend le pilote d'origine (W1-2)
//   --console         relaie l'adaptateur par USB/IP jusqu'à Ctrl+C, puis le rend (W1-3)
//   --restore-native  secours : rend leur pilote d'origine à tous les adaptateurs passés sur WinUSB

string command = args.FirstOrDefault() ?? "";
if (command is not ("--status" or "--takeover-test" or "--console" or "--restore-native"))
{
    Console.WriteLine("Utilisation : acaw-service --status | --takeover-test | --console | --restore-native");
    return 2;
}
if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
{
    Console.WriteLine("Cette commande exige un terminal ouvert en administrateur.");
    return 5;
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
