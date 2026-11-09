// ACAW en ligne de commande. La bascule automatique est faite par acaw-tray.exe.
// Mode d'emploi : docs/guide/06-utiliser-acaw.md

using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Acaw.Core.Ble;
using Acaw.Core.Config;
using Acaw.Core.Hosting;
using Acaw.Windows.Ble;
using Acaw.Windows.Bluetooth;

Console.OutputEncoding = Encoding.UTF8;

const string Usage = """
    Utilisation : acaw <commande>

      setup [numéro]   choisir les AirPods à piloter (la liste s'affiche sans numéro)
      status           état des AirPods : connexion à ce PC, éveil, écouteurs portés, batteries
      switch           amener les AirPods sur ce PC
      release          rendre les AirPods (l'iPhone les reprend en quelques secondes)
      startup on|off   démarrer ou non acaw-tray avec Windows (sans argument : l'état actuel)
      quit             arrêter acaw-tray proprement (le son coupé par ACAW est rétabli)
      handsfree on|off micro des AirPods (profil mains libres) : on = micro pour les appels, bascules
                       lentes ; off = bascules rapides (sans argument : l'état actuel)
      watch            mesure : affiche la liaison radio, la sortie audio et le journal d'ACAW sur
                       une même ligne de temps (Ctrl+C pour arrêter)
      --version        la version d'ACAW
    """;
const string BluetoothOff = "Bluetooth du PC coupé (ou casque absent pour Windows).";

string configPath = ConfigStore.DefaultPath;
string? command = args.FirstOrDefault();
var config = new AcawConfig();

try
{
    // Ces deux commandes ne lisent pas la configuration : elles marchent même si config.json est abîmé
    // (l'installateur s'en sert).
    if (command is "--version" or "version")
    {
        Console.WriteLine($"ACAW {Acaw.Core.AppInfo.Version}");
        return 0;
    }
    if (command == "startup") return Startup(args.ElementAtOrDefault(1));
    if (command == "quit")
    {
        // Arrêt propre de acaw-tray (son rétabli) ; l'installateur le fait avant de remplacer les fichiers.
        bool stopped = new Acaw.Windows.QuitSignal().Request(TimeSpan.FromSeconds(10));
        Console.WriteLine(stopped ? "ACAW est arrêté." : "ACAW ne s'est pas arrêté en 10 s.");
        return stopped ? 0 : 2;
    }

    config = LoadConfig();
    return command switch
    {
        "setup" => Setup(args.ElementAtOrDefault(1)),
        "status" => Status(),
        "switch" => Switch(),
        "release" => Release(),
        "handsfree" => HandsFree(args.ElementAtOrDefault(1)),
        "watch" => Watch(),
        _ => Fail(Usage),
    };
}
catch (UserError e)
{
    Console.WriteLine(e.Message);
    return 1;
}
catch (InvalidDataException e)
{
    Console.WriteLine($"{e.Message}{Environment.NewLine}Relance « acaw setup » pour la recréer.");
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

// Démarrage avec Windows de acaw-tray.exe, installé à côté de acaw.exe. Même code que la case du menu de
// l'icône (StartupShortcut) : l'installateur l'appelle avec « on » ou « off ».
int Startup(string? action)
{
    string tray = Path.Combine(AppContext.BaseDirectory, "acaw-tray.exe");
    var shortcut = Acaw.Windows.StartupShortcut.ForCurrentUser(tray);
    try
    {
        switch (action)
        {
            case "on":
                if (!File.Exists(tray)) throw new UserError($"acaw-tray.exe introuvable à côté de acaw.exe ({tray}).");
                shortcut.Set(true);
                Console.WriteLine($"Démarrage avec Windows activé ({shortcut.ShortcutPath}).");
                return 0;
            case "off":
                shortcut.Set(false);
                Console.WriteLine("Démarrage avec Windows désactivé.");
                return 0;
            case null or "status":
                Console.WriteLine(shortcut.IsEnabled ? "Démarrage avec Windows : activé" : "Démarrage avec Windows : désactivé");
                return 0;
            default:
                return Fail(Usage);
        }
    }
    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
    {
        throw new UserError($"Réglage du démarrage impossible : {e.Message}");
    }
}

// Micro des AirPods = profil mains libres. Même code que la case du menu de l'icône ; le choix est retenu
// dans config.json (Microphone), pour qu'ACAW n'avertisse pas des bascules lentes si c'est voulu.
int HandsFree(string? action)
{
    RequireEndpoint();
    if (action is null or "status")
    {
        Console.WriteLine(HandsFreeProfile.IsEnabled(config.Mac!)
            ? "Micro des AirPods : actif (bascules lentes, ~10 s)"
            : "Micro des AirPods : retiré (bascules rapides, ~2 s)");
        return 0;
    }
    if (action is not ("on" or "off")) return Fail(Usage);

    bool on = action == "on";
    // acaw-tray, lui, ignore les coupures du lien pendant la bascule ; depuis ici, il ne le sait pas.
    if (Process.GetProcessesByName("acaw-tray").Length > 0)
        Console.WriteLine("Conseil : ACAW tourne ; la case « Micro des AirPods » de son menu évite qu'il prenne les coupures pour une perte.");
    Console.Write(on ? "Activation du micro (~3 s)… " : "Retrait du micro (~3 s)… ");
    var outcome = HandsFreeProfile.SetEnabled(config.Mac!, on);
    Console.WriteLine();
    Console.WriteLine(HandsFreeProfile.Describe(outcome.Result, on));
    if (!outcome.Succeeded)
    {
        Console.WriteLine($"(code Windows {outcome.Code} / 0x{outcome.Code:X8})");
        return 2;
    }
    new FileConfigSource(configPath).Save(ConfigStore.Load(configPath) with { Microphone = on });
    return 0;
}

// Mesure de l'étape 0 « bascule suivie » (guide 4, test L) : la liaison radio s'établit-elle nettement avant la
// sortie audio ? Trois sources, plus le journal d'acaw-tray, sur une seule ligne de temps. Passif : ne commande
// rien, acaw-tray continue de basculer comme d'habitude.
int Watch()
{
    var ep = RequireEndpoint();
    string mac = config.Mac!;
    var gate = new Lock();
    DateTime? reconnectAt = null;

    void Print(DateTime at, string source, string text)
    {
        lock (gate)
        {
            string since = reconnectAt is { } t && at >= t ? $"   [+{(at - t).TotalSeconds:F2} s depuis Reconnect]" : "";
            Console.WriteLine($"{at:HH:mm:ss.fff}  {source,-16}{text}{since}");
        }
    }

    // 1. La liaison radio selon WinRT : un événement, poussé par Windows.
    using var device = global::Windows.Devices.Bluetooth.BluetoothDevice
        .FromBluetoothAddressAsync(HandsFreeProfile.ParseAddress(mac)).AsTask().GetAwaiter().GetResult();
    if (device is not null)
    {
        Print(DateTime.Now, "liaison WinRT", $"{device.ConnectionStatus} (départ)");
        device.ConnectionStatusChanged += (d, _) => Print(DateTime.Now, "liaison WinRT", d.ConnectionStatus.ToString());
    }
    else Print(DateTime.Now, "liaison WinRT", "appareil introuvable pour WinRT");

    using var stop = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
    Console.WriteLine($"Mesure en cours pour {ep.Name}. Fais tes bascules ; Ctrl+C pour arrêter.");

    // 2 et 3. La liaison selon Win32 et la sortie audio, relevées toutes les 50 ms. Plus le journal d'acaw-tray.
    long journalAt = File.Exists(Acaw.Windows.FileLog.Path) ? new FileInfo(Acaw.Windows.FileLog.Path).Length : 0;
    string pending = "";
    bool first = true;
    bool? lastLink = null;
    HeadsetState lastAudio = default;
    BtEndpoint? handsFree = null;
    bool micEnabled = false;
    string lastMic = "";
    int turn = 0;
    while (!stop.IsCancellationRequested)
    {
        bool? link = BluetoothLink.IsConnected(mac);
        if (first || link != lastLink)
            Print(DateTime.Now, "liaison Win32", link switch { true => "connectée", false => "déconnectée", null => "illisible" });
        lastLink = link;

        HeadsetState audio;
        try { audio = BluetoothAudio.GetState(ep.Id); }
        catch (COMException) { audio = HeadsetState.Absent; }
        if (first || audio != lastAudio) Print(DateTime.Now, "sortie audio", audio.ToString());
        lastAudio = audio;

        // Le micro (profil mains libres) : recherché chaque seconde, car il apparaît et disparaît avec la case.
        // Le filtre du driver dit si le profil est activé ; la sortie audio, souvent invisible, dit s'il est connecté.
        if (++turn % 20 == 1)
        {
            micEnabled = BluetoothAudio.HandsFreeFilterFor(mac) is not null;
            handsFree = micEnabled ? BluetoothAudio.FindHandsFree(mac) : null;
        }
        string mic;
        try
        {
            mic = !micEnabled ? "profil retiré"
                : handsFree is null ? "profil activé (sortie audio invisible)"
                : BluetoothAudio.GetState(handsFree.Id).ToString();
        }
        catch (COMException) { mic = "profil activé (sortie audio invisible)"; handsFree = null; }
        if (first || mic != lastMic) Print(DateTime.Now, "micro (HFP)", mic);
        lastMic = mic;
        first = false;

        (journalAt, pending) = ReadJournal(journalAt, pending, line =>
        {
            DateTime at = DateTime.TryParseExact(line.AsSpan(0, Math.Min(23, line.Length)), "yyyy-MM-dd HH:mm:ss.fff",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : DateTime.Now;
            if (line.Contains("Commande Reconnect", StringComparison.Ordinal))
            {
                lock (gate) reconnectAt = at;
                Console.WriteLine("──────────────────────────────────────────────────────────────");
            }
            Print(at, "journal", line.Length > 25 ? line[25..] : line);
        });
        stop.Token.WaitHandle.WaitOne(50);
    }
    return 0;
}

// Les lignes ajoutées au journal depuis la dernière lecture (le journal reste ouvert en écriture par acaw-tray).
static (long Position, string Pending) ReadJournal(long position, string pending, Action<string> onLine)
{
    string path = Acaw.Windows.FileLog.Path;
    try
    {
        if (!File.Exists(path)) return (0, "");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length < position) position = 0; // journal renommé (> 1 Mo) : on repart du début
        if (stream.Length == position) return (position, pending);
        stream.Position = position;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        string text = pending + reader.ReadToEnd();
        int end = text.LastIndexOf('\n');
        if (end < 0) return (stream.Length, text);
        foreach (string line in text[..end].Split('\n')) if (line.TrimEnd('\r') is { Length: > 0 } l) onLine(l);
        return (stream.Length, text[(end + 1)..]);
    }
    catch (IOException)
    {
        return (position, pending); // journal en cours d'écriture : on réessaiera au prochain tour
    }
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
    Console.WriteLine(HandsFreeProfile.IsEnabled(config.Mac!)
        ? "Profil mains libres : ACTIF → bascules lentes (10 s). Le retirer : docs/guide/06-utiliser-acaw.md"
        : "Profil mains libres : retiré (bien)");

    if (config.ModelNumber is not { } model) return 0;
    Console.Write("Écoute BLE pendant 5 s… ");
    ProximityFrame? last = null;
    using (var scanner = new BleScanner(model))
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
    if (state == HeadsetState.Connected) throw new UserError("Les AirPods sont déjà sur ce PC.");
    if (state == HeadsetState.Absent) throw new UserError(BluetoothOff);

    var (ok, elapsed) = SendAndWait(ep, BluetoothAudio.Connect, s => s == HeadsetState.Connected);
    Console.WriteLine(ok
        ? $"AirPods sur ce PC en {elapsed.TotalMilliseconds:F0} ms."
        : "Les AirPods ne sont pas venus. Boîtier fermé ou hors de portée ?");
    return ok ? 0 : 2;
}

int Release()
{
    var ep = RequireEndpoint();
    if (BluetoothAudio.GetState(ep.Id) != HeadsetState.Connected)
        throw new UserError("Les AirPods ne sont pas sur ce PC.");

    var (ok, _) = SendAndWait(ep, BluetoothAudio.Disconnect, s => s != HeadsetState.Connected);
    // Le micro, s'il est activé, garderait les AirPods accrochés au PC (tests L3 et H9).
    if (BluetoothAudio.ReleaseHandsFree(config.Mac!) is { } code && code != 0)
        Console.WriteLine($"Relâche du micro refusée (code Windows {code}) : l'iPhone pourrait ne pas reprendre les AirPods.");
    Console.WriteLine(ok ? "AirPods rendus : l'iPhone va les reprendre." : "Les AirPods sont toujours connectés.");
    return ok ? 0 : 2;
}

// Envoie l'ordre puis attend que Windows signale le nouvel état. On s'abonne AVANT d'envoyer l'ordre,
// pour ne pas rater une notification qui arriverait très vite.
(bool Ok, TimeSpan Elapsed) SendAndWait(BtEndpoint ep, Action<BtEndpoint> order, Func<HeadsetState, bool> done)
{
    using var reached = new ManualResetEventSlim();
    using var watcher = new EndpointWatcher(ep.Id);
    watcher.StateChanged += state => { if (done(state)) reached.Set(); };

    var clock = Stopwatch.StartNew();
    order(ep);
    bool ok = reached.Wait(config.Switching.SwitchTimeout)
              || done(BluetoothAudio.GetState(ep.Id)); // filet de sécurité si la notification a été manquée
    return (ok, clock.Elapsed);
}

// Une configuration abîmée bloque toutes les commandes, sauf setup qui sert justement à la refaire.
AcawConfig LoadConfig()
{
    try
    {
        return ConfigStore.Load(configPath);
    }
    catch (InvalidDataException) when (command == "setup")
    {
        Console.WriteLine("Configuration illisible : elle va être remplacée.");
        return new AcawConfig();
    }
}

BtEndpoint RequireEndpoint()
{
    if (!config.IsConfigured) throw new UserError("Aucun AirPods configuré. Lance d'abord « acaw setup ».");
    return BluetoothAudio.FindA2dp(config.Mac!)
        ?? throw new UserError($"{config.Name} n'est plus appairé à ce PC. Ré-appaire-le puis relance « acaw setup ».");
}

static string Describe(HeadsetState state) => state switch
{
    HeadsetState.Connected => "connectés",
    HeadsetState.Disconnected => "non (ailleurs ou libres)",
    _ => BluetoothOff,
};

static string Pct(int? value) => value is null ? "—" : $"{value} %";

static int Fail(string message)
{
    Console.WriteLine(message);
    return 1;
}

/// <summary>Erreur d'utilisation : on affiche le message sans pile d'appels.</summary>
sealed class UserError(string message) : Exception(message);
