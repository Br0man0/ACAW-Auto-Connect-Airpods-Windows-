// ACAW en ligne de commande (phase 1). La bascule automatique arrivera en phase 2.
// Mode d'emploi : docs/guide/06-utiliser-acaw.md

using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Acaw.App.Ble;
using Acaw.App.Bluetooth;
using Acaw.Core.Ble;
using Acaw.Core.Config;
using Windows.Win32.Media.Audio;
using Windows.Win32.Media.KernelStreaming;

Console.OutputEncoding = Encoding.UTF8;

const string Usage = """
    Utilisation : acaw <commande>

      setup [numéro]   choisir les AirPods à piloter (la liste s'affiche sans numéro)
      status           état des AirPods : connexion à ce PC, éveil, écouteurs portés, batteries
      switch           amener les AirPods sur ce PC
      release          rendre les AirPods (l'iPhone les reprend en quelques secondes)
    """;
const string BluetoothOff = "Bluetooth du PC coupé (ou casque absent pour Windows).";

string configPath = ConfigStore.DefaultPath;
var config = ConfigStore.Load(configPath);

try
{
    return args.FirstOrDefault() switch
    {
        "setup" => Setup(args.ElementAtOrDefault(1)),
        "status" => Status(),
        "switch" => Switch(),
        "release" => Release(),
        _ => Fail(Usage),
    };
}
catch (UserError e)
{
    Console.WriteLine(e.Message);
    return 1;
}
catch (InvalidCastException)
{
    Console.WriteLine("Windows refuse l'accès au driver Bluetooth de ce casque (E_NOINTERFACE). " +
                      "Vérifie que le casque est bien appairé, ou ré-appaire-le.");
    return 3;
}
catch (COMException e) when (e.HResult == unchecked((int)0x800710DF))
{
    Console.WriteLine("Le Bluetooth du PC est indisponible. Active-le dans Paramètres → Bluetooth et appareils.");
    return 3;
}
catch (COMException e)
{
    Console.WriteLine($"Erreur Windows 0x{e.HResult:X8} : {e.Message}");
    return 3;
}

int Setup(string? choice)
{
    var headsets = BluetoothAudio.ListEndpoints().Where(ep => ep.IsA2dp && ep.Mac is not null).ToList();
    if (headsets.Count == 0)
        throw new UserError("Aucun casque Bluetooth appairé. Appaire d'abord les AirPods dans les paramètres Windows.");

    if (choice is null)
    {
        for (int i = 0; i < headsets.Count; i++)
            Console.WriteLine($"  {i + 1}. {headsets[i].Name}  (modèle {headsets[i].ModelId ?? "?"})");
        Console.Write("Numéro des AirPods à piloter : ");
        choice = Console.ReadLine();
    }

    if (!int.TryParse(choice, CultureInfo.InvariantCulture, out int n) || n < 1 || n > headsets.Count)
        throw new UserError($"Choix invalide : indique un numéro entre 1 et {headsets.Count}.");

    var chosen = headsets[n - 1];
    ConfigStore.Save(configPath, config with { Mac = chosen.Mac, ModelId = chosen.ModelId, Name = chosen.Name });
    Console.WriteLine($"Enregistré : {chosen.Name}. Configuration dans {configPath}");
    return 0;
}

int Status()
{
    var ep = RequireEndpoint();
    Console.WriteLine($"AirPods : {ep.Name} (modèle {config.ModelId})");
    Console.WriteLine($"Sur ce PC : {Describe(BluetoothAudio.GetState(ep.Id))}");

    if (config.ModelId is null) return 0;
    Console.Write("Écoute BLE pendant 5 s… ");
    ProximityFrame? last = null;
    using (var scanner = new BleScanner(config.ModelId))
    {
        scanner.FrameReceived += frame => last = frame;
        scanner.Start();
        Thread.Sleep(TimeSpan.FromSeconds(5));
    }

    Console.WriteLine();
    if (last is null)
    {
        Console.WriteLine("Éveillés : non détectés (boîtier fermé ou hors de portée)");
        return 0;
    }
    Console.WriteLine($"Éveillés : oui · écouteurs portés : {last.PodsInEar} · dans le boîtier : {last.PodsInCase}");
    Console.WriteLine($"Batteries : écouteurs {Pct(last.BatteryA)} / {Pct(last.BatteryB)} · boîtier {Pct(last.CaseBattery)}");
    return 0;
}

int Switch()
{
    var ep = RequireEndpoint();
    var state = BluetoothAudio.GetState(ep.Id);
    if (state == DEVICE_STATE.DEVICE_STATE_ACTIVE) throw new UserError("Les AirPods sont déjà sur ce PC.");
    if (state == DEVICE_STATE.DEVICE_STATE_NOTPRESENT) throw new UserError(BluetoothOff);

    var (ok, elapsed) = SendAndWait(ep, KSPROPERTY_BTAUDIO.KSPROPERTY_ONESHOT_RECONNECT,
                                    s => s == DEVICE_STATE.DEVICE_STATE_ACTIVE);
    Console.WriteLine(ok
        ? $"AirPods sur ce PC en {elapsed.TotalMilliseconds:F0} ms."
        : "Les AirPods ne sont pas venus. Boîtier fermé ou hors de portée ?");
    return ok ? 0 : 2;
}

int Release()
{
    var ep = RequireEndpoint();
    if (BluetoothAudio.GetState(ep.Id) != DEVICE_STATE.DEVICE_STATE_ACTIVE)
        throw new UserError("Les AirPods ne sont pas sur ce PC.");

    var (ok, _) = SendAndWait(ep, KSPROPERTY_BTAUDIO.KSPROPERTY_ONESHOT_DISCONNECT,
                              s => s != DEVICE_STATE.DEVICE_STATE_ACTIVE);
    Console.WriteLine(ok ? "AirPods rendus : l'iPhone va les reprendre." : "Les AirPods sont toujours connectés.");
    return ok ? 0 : 2;
}

// Envoie l'ordre puis attend que Windows signale le nouvel état. On s'abonne AVANT d'envoyer l'ordre,
// pour ne pas rater une notification qui arriverait très vite.
(bool Ok, TimeSpan Elapsed) SendAndWait(BtEndpoint ep, KSPROPERTY_BTAUDIO command, Func<DEVICE_STATE, bool> done)
{
    using var reached = new ManualResetEventSlim();
    using var watcher = new EndpointWatcher(ep.Id);
    watcher.StateChanged += state => { if (done(state)) reached.Set(); };

    var clock = Stopwatch.StartNew();
    BluetoothAudio.SendOneShot(ep, command);
    bool ok = reached.Wait(config.Switching.SwitchTimeout)
              || done(BluetoothAudio.GetState(ep.Id)); // filet de sécurité si la notification a été manquée
    return (ok, clock.Elapsed);
}

BtEndpoint RequireEndpoint()
{
    if (!config.IsConfigured) throw new UserError("Aucun AirPods configuré. Lance d'abord « acaw setup ».");
    return BluetoothAudio.FindA2dp(config.Mac!)
        ?? throw new UserError($"{config.Name} n'est plus appairé à ce PC. Ré-appaire-le puis relance « acaw setup ».");
}

static string Describe(DEVICE_STATE state) => state switch
{
    DEVICE_STATE.DEVICE_STATE_ACTIVE => "connectés",
    DEVICE_STATE.DEVICE_STATE_UNPLUGGED => "non (ailleurs ou libres)",
    DEVICE_STATE.DEVICE_STATE_NOTPRESENT => BluetoothOff,
    _ => state.ToString(),
};

static string Pct(int? value) => value is null ? "—" : $"{value} %";

static int Fail(string message)
{
    Console.WriteLine(message);
    return 1;
}

/// <summary>Erreur d'utilisation : on affiche le message sans pile d'appels.</summary>
sealed class UserError(string message) : Exception(message);
