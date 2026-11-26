// ACAW en ligne de commande. La bascule automatique est faite par acaw-tray.exe.

using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Acaw.Core;
using Acaw.Core.Ble;
using Acaw.Core.Config;
using Acaw.Core.Hosting;
using Acaw.Windows.Ble;
using Acaw.Windows.Bluetooth;

Console.OutputEncoding = Encoding.UTF8;

string configPath = ConfigStore.DefaultPath;
string? command = args.FirstOrDefault();
var config = new AcawConfig();

// La langue avant tout message ; config.json absent ou abîmé : celle de Windows.
try
{
    Lang.Current = Lang.Resolve(ConfigStore.Load(configPath).Language);
}
catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
{
    Lang.Current = Lang.FromWindows();
}

string usage = Lang.Pick("""
    Utilisation : acaw <commande>

      setup [numéro]     choisir les AirPods à piloter (la liste s'affiche sans numéro)
      status             état des AirPods : connexion à ce PC, éveil, écouteurs portés, batteries
      switch             amener les AirPods sur ce PC
      release            rendre les AirPods (l'iPhone les reprend en quelques secondes)
      startup on|off     démarrer ou non acaw-tray avec Windows (sans argument : l'état actuel)
      quit               arrêter acaw-tray proprement (le son coupé par ACAW est rétabli)
      handsfree on|off   micro des AirPods (profil mains libres) : on = micro pour les appels, bascules
                         lentes ; off = bascules rapides (sans argument : l'état actuel)
      language fr|en|auto  langue d'ACAW ; auto = celle de Windows (sans argument : la langue actuelle)
      watch              mesure : affiche la liaison radio, la sortie audio et le journal d'ACAW sur
                         une même ligne de temps (Ctrl+C pour arrêter)
      --version          la version d'ACAW
    """, """
    Usage: acaw <command>

      setup [number]     choose the AirPods to drive (lists them without a number)
      status             AirPods state: connection to this PC, awake, earbuds worn, batteries
      switch             bring the AirPods to this PC
      release            give the AirPods back (the iPhone takes them back within seconds)
      startup on|off     start acaw-tray with Windows or not (no argument: current state)
      quit               stop acaw-tray cleanly (sound muted by ACAW is restored)
      handsfree on|off   AirPods microphone (hands-free profile): on = microphone for calls, slow
                         switching; off = fast switching (no argument: current state)
      language fr|en|auto  ACAW language; auto = the Windows language (no argument: current language)
      watch              measurement: shows the radio link, the audio output and the ACAW log on
                         one timeline (Ctrl+C to stop)
      --version          the ACAW version
    """);
string bluetoothOff = Lang.Pick("Bluetooth du PC coupé (ou casque absent pour Windows).",
                                "The PC Bluetooth is off (or Windows does not see the headset).");

try
{
    // Ces deux commandes ne lisent pas la configuration : elles marchent même si config.json est abîmé
    // (l'installateur s'en sert).
    if (command is "--version" or "version")
    {
        Console.WriteLine($"ACAW {AppInfo.Version}");
        return 0;
    }
    if (command == "startup") return Startup(args.ElementAtOrDefault(1));
    if (command == "quit")
    {
        // Arrêt propre de acaw-tray (son rétabli) ; l'installateur le fait avant de remplacer les fichiers.
        bool stopped = new Acaw.Windows.QuitSignal().Request(TimeSpan.FromSeconds(10));
        Console.WriteLine(stopped ? Lang.Pick("ACAW est arrêté.", "ACAW has stopped.")
                                  : Lang.Pick("ACAW ne s'est pas arrêté en 10 s.", "ACAW did not stop within 10 s."));
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
        "language" => Language(args.ElementAtOrDefault(1)),
        "watch" => Watch(),
        _ => Fail(usage),
    };
}
catch (UserError e)
{
    Console.WriteLine(e.Message);
    return 1;
}
catch (InvalidDataException e)
{
    Console.WriteLine($"{e.Message}{Environment.NewLine}" +
                      Lang.Pick("Relance « acaw setup » pour la recréer.", "Run \"acaw setup\" again to recreate it."));
    return 1;
}
catch (InvalidCastException)
{
    Console.WriteLine(Lang.Pick(
        "Windows refuse l'accès au driver Bluetooth de ce casque (E_NOINTERFACE). Vérifie que le casque est bien appairé, ou ré-appaire-le.",
        "Windows denies access to this headset's Bluetooth driver (E_NOINTERFACE). Check that the headset is paired, or pair it again."));
    return 3;
}
catch (COMException e) when (e.HResult == unchecked((int)0x800710DF))
{
    Console.WriteLine(Lang.Pick("Le Bluetooth du PC est indisponible. Active-le dans Paramètres → Bluetooth et appareils.",
                                "The PC Bluetooth is unavailable. Turn it on in Settings → Bluetooth & devices."));
    return 3;
}
catch (COMException e)
{
    Console.WriteLine(Lang.Pick($"Erreur Windows 0x{e.HResult:X8} : {e.Message}", $"Windows error 0x{e.HResult:X8}: {e.Message}"));
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
                if (!File.Exists(tray))
                    throw new UserError(Lang.Pick($"acaw-tray.exe introuvable à côté de acaw.exe ({tray}).",
                                                  $"acaw-tray.exe not found next to acaw.exe ({tray})."));
                shortcut.Set(true);
                Console.WriteLine(Lang.Pick($"Démarrage avec Windows activé ({shortcut.ShortcutPath}).",
                                            $"Start with Windows on ({shortcut.ShortcutPath})."));
                return 0;
            case "off":
                shortcut.Set(false);
                Console.WriteLine(Lang.Pick("Démarrage avec Windows désactivé.", "Start with Windows off."));
                return 0;
            case null or "status":
                Console.WriteLine(shortcut.IsEnabled ? Lang.Pick("Démarrage avec Windows : activé", "Start with Windows: on")
                                                     : Lang.Pick("Démarrage avec Windows : désactivé", "Start with Windows: off"));
                return 0;
            default:
                return Fail(usage);
        }
    }
    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
    {
        throw new UserError(Lang.Pick($"Réglage du démarrage impossible : {e.Message}", $"Could not change the startup setting: {e.Message}"));
    }
}

// Langue d'ACAW, enregistrée dans config.json : l'installateur l'appelle avec le choix fait au début de
// l'installation. acaw-tray, s'il tourne, relit le fichier et change de langue tout seul.
int Language(string? choice)
{
    if (choice is null or "status")
    {
        string setting = config.Language is null ? Lang.Pick(" (celle de Windows)", " (the Windows language)") : "";
        Console.WriteLine(Lang.Pick($"Langue : français{setting}", $"Language: English{setting}"));
        return 0;
    }
    string? code = choice.ToLowerInvariant() switch
    {
        "fr" => "fr",
        "en" => "en",
        "auto" => null,
        _ => "?",
    };
    if (code == "?") return Fail(usage);

    new FileConfigSource(configPath).Save(config with { Language = code });
    Lang.Current = Lang.Resolve(code);
    string auto = code is null ? Lang.Pick(" (celle de Windows)", " (the Windows language)") : "";
    Console.WriteLine(Lang.Pick($"Langue enregistrée : français{auto}.", $"Language saved: English{auto}."));
    return 0;
}

// Micro des AirPods = profil mains libres. Même code que la case du menu de l'icône ; le choix est retenu
// dans config.json (Microphone), pour qu'ACAW n'avertisse pas des bascules lentes si c'est voulu.
int HandsFree(string? action)
{
    RequireEndpoint();
    if (action is null or "status")
    {
        Console.WriteLine(HandsFreeProfile.IsEnabled(config.Mac!)
            ? Lang.Pick("Micro des AirPods : actif (bascules lentes, ~10 s)", "AirPods microphone: on (slow switching, ~10 s)")
            : Lang.Pick("Micro des AirPods : retiré (bascules rapides, ~2 s)", "AirPods microphone: off (fast switching, ~2 s)"));
        return 0;
    }
    if (action is not ("on" or "off")) return Fail(usage);

    bool on = action == "on";
    // acaw-tray, lui, ignore les coupures du lien pendant la bascule ; depuis ici, il ne le sait pas.
    if (Process.GetProcessesByName("acaw-tray").Length > 0)
        Console.WriteLine(Lang.Pick(
            "Conseil : ACAW tourne ; la case « Micro des AirPods » de son menu évite qu'il prenne les coupures pour une perte.",
            "Tip: ACAW is running; its \"AirPods microphone\" menu item keeps it from mistaking the drop-outs for a loss."));
    Console.Write(on ? Lang.Pick("Activation du micro (~3 s)… ", "Turning the microphone on (~3 s)… ")
                     : Lang.Pick("Retrait du micro (~3 s)… ", "Turning the microphone off (~3 s)… "));
    var outcome = HandsFreeProfile.SetEnabled(config.Mac!, on);
    Console.WriteLine();
    Console.WriteLine(HandsFreeProfile.Describe(outcome.Result, on));
    if (!outcome.Succeeded)
    {
        Console.WriteLine(Lang.Pick($"(code Windows {outcome.Code} / 0x{outcome.Code:X8})", $"(Windows code {outcome.Code} / 0x{outcome.Code:X8})"));
        return 2;
    }
    new FileConfigSource(configPath).Save(ConfigStore.Load(configPath) with { Microphone = on });
    return 0;
}

// Outil de mesure : la liaison radio s'établit-elle avant la sortie audio ? Trois sources, plus le journal
// d'acaw-tray, sur une seule ligne de temps. Passif : ne commande rien, acaw-tray continue de basculer.
int Watch()
{
    var ep = RequireEndpoint();
    string mac = config.Mac!;
    var gate = new Lock();
    DateTime? reconnectAt = null;
    string linkWinRt = Lang.Pick("liaison WinRT", "WinRT link");
    string linkWin32 = Lang.Pick("liaison Win32", "Win32 link");
    string audioOut = Lang.Pick("sortie audio", "audio output");
    string micOut = Lang.Pick("micro (HFP)", "mic (HFP)");
    string journal = Lang.Pick("journal", "log");

    void Print(DateTime at, string source, string text)
    {
        lock (gate)
        {
            string since = reconnectAt is { } t && at >= t
                ? Lang.Pick($"   [+{(at - t).TotalSeconds:F2} s depuis Reconnect]", $"   [+{(at - t).TotalSeconds:F2} s since Reconnect]")
                : "";
            Console.WriteLine($"{at:HH:mm:ss.fff}  {source,-16}{text}{since}");
        }
    }

    // 1. La liaison radio selon WinRT : un événement, poussé par Windows.
    using var device = global::Windows.Devices.Bluetooth.BluetoothDevice
        .FromBluetoothAddressAsync(HandsFreeProfile.ParseAddress(mac)).AsTask().GetAwaiter().GetResult();
    if (device is not null)
    {
        Print(DateTime.Now, linkWinRt, $"{device.ConnectionStatus} ({Lang.Pick("départ", "start")})");
        device.ConnectionStatusChanged += (d, _) => Print(DateTime.Now, linkWinRt, d.ConnectionStatus.ToString());
    }
    else Print(DateTime.Now, linkWinRt, Lang.Pick("appareil introuvable pour WinRT", "device not found by WinRT"));

    using var stop = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
    Console.WriteLine(Lang.Pick($"Mesure en cours pour {ep.Name}. Fais tes bascules ; Ctrl+C pour arrêter.",
                                $"Measuring {ep.Name}. Do your switches; Ctrl+C to stop."));

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
    string micOff = Lang.Pick("profil retiré", "profile off");
    string micHidden = Lang.Pick("profil activé (sortie audio invisible)", "profile on (audio output not visible)");
    while (!stop.IsCancellationRequested)
    {
        bool? link = BluetoothLink.IsConnected(mac);
        if (first || link != lastLink)
            Print(DateTime.Now, linkWin32, link switch
            {
                true => Lang.Pick("connectée", "connected"),
                false => Lang.Pick("déconnectée", "disconnected"),
                null => Lang.Pick("illisible", "unreadable"),
            });
        lastLink = link;

        HeadsetState audio;
        try { audio = BluetoothAudio.GetState(ep.Id); }
        catch (COMException) { audio = HeadsetState.Absent; }
        if (first || audio != lastAudio) Print(DateTime.Now, audioOut, audio.ToString());
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
            mic = !micEnabled ? micOff
                : handsFree is null ? micHidden
                : BluetoothAudio.GetState(handsFree.Id).ToString();
        }
        catch (COMException) { mic = micHidden; handsFree = null; }
        if (first || mic != lastMic) Print(DateTime.Now, micOut, mic);
        lastMic = mic;
        first = false;

        (journalAt, pending) = ReadJournal(journalAt, pending, line =>
        {
            DateTime at = DateTime.TryParseExact(line.AsSpan(0, Math.Min(23, line.Length)), "yyyy-MM-dd HH:mm:ss.fff",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : DateTime.Now;
            // Le journal peut être dans l'une ou l'autre langue (acaw-tray a pu changer de langue entre-temps).
            if (line.Contains("Commande Reconnect", StringComparison.Ordinal) || line.Contains("Command Reconnect", StringComparison.Ordinal))
            {
                lock (gate) reconnectAt = at;
                Console.WriteLine("──────────────────────────────────────────────────────────────");
            }
            Print(at, journal, line.Length > 25 ? line[25..] : line);
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
        throw new UserError(Lang.Pick("Aucun casque Bluetooth appairé. Appaire d'abord les AirPods dans les paramètres Windows.",
                                      "No Bluetooth headset paired. Pair the AirPods in the Windows settings first."));

    if (choice is null)
    {
        for (int i = 0; i < headsets.Count; i++)
            Console.WriteLine($"  {i + 1}. {headsets[i].Name}  ({Lang.Pick("modèle", "model")} {headsets[i].ModelId ?? "?"})");
        Console.Write(Lang.Pick("Numéro des AirPods à piloter : ", "Number of the AirPods to drive: "));
        choice = Console.ReadLine();
    }

    if (!int.TryParse(choice, CultureInfo.InvariantCulture, out int n) || n < 1 || n > headsets.Count)
        throw new UserError(Lang.Pick($"Choix invalide : indique un numéro entre 1 et {headsets.Count}.",
                                      $"Invalid choice: enter a number between 1 and {headsets.Count}."));

    var chosen = headsets[n - 1];
    ConfigStore.Save(configPath, config with { Mac = chosen.Mac, ModelId = chosen.ModelId, Name = chosen.Name });
    Console.WriteLine(Lang.Pick($"Enregistré : {chosen.Name}. Configuration dans {configPath}",
                                $"Saved: {chosen.Name}. Configuration in {configPath}"));
    return 0;
}

int Status()
{
    var ep = RequireEndpoint();
    Console.WriteLine($"AirPods : {ep.Name} ({Lang.Pick("modèle", "model")} {config.ModelId})");
    Console.WriteLine($"{Lang.Pick("Sur ce PC", "On this PC")} : {Describe(BluetoothAudio.GetState(ep.Id))}");
    Console.WriteLine(HandsFreeProfile.IsEnabled(config.Mac!)
        ? Lang.Pick("Profil mains libres : ACTIF → bascules lentes (10 s). Pour le retirer : menu de l'icône ACAW → Micro des AirPods, ou « acaw handsfree off »",
                    "Hands-free profile: ON → slow switching (10 s). To turn it off: ACAW icon menu → AirPods microphone, or \"acaw handsfree off\"")
        : Lang.Pick("Profil mains libres : retiré (bien)", "Hands-free profile: off (good)"));

    if (config.ModelNumber is not { } model) return 0;
    Console.Write(Lang.Pick("Écoute BLE pendant 5 s… ", "Listening to BLE for 5 s… "));
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
        Console.WriteLine(Lang.Pick("Éveillés : non détectés (boîtier fermé ou hors de portée)", "Awake: not detected (case closed or out of range)"));
        return 0;
    }
    Console.WriteLine(Lang.Pick($"Éveillés : oui · écouteurs portés : {last.PodsInEar} · dans le boîtier : {last.PodsInCase}",
                                $"Awake: yes · earbuds worn: {last.PodsInEar} · in the case: {last.PodsInCase}"));
    Console.WriteLine(Lang.Pick($"Batteries : écouteurs {Pct(last.BatteryA)} / {Pct(last.BatteryB)} · boîtier {Pct(last.CaseBattery)}",
                                $"Batteries: earbuds {Pct(last.BatteryA)} / {Pct(last.BatteryB)} · case {Pct(last.CaseBattery)}"));
    return 0;
}

int Switch()
{
    var ep = RequireEndpoint();
    var state = BluetoothAudio.GetState(ep.Id);
    if (state == HeadsetState.Connected) throw new UserError(Lang.Pick("Les AirPods sont déjà sur ce PC.", "The AirPods are already on this PC."));
    if (state == HeadsetState.Absent) throw new UserError(bluetoothOff);

    var (ok, elapsed) = SendAndWait(ep, BluetoothAudio.Connect, s => s == HeadsetState.Connected);
    Console.WriteLine(ok
        ? Lang.Pick($"AirPods sur ce PC en {elapsed.TotalMilliseconds:F0} ms.", $"AirPods on this PC in {elapsed.TotalMilliseconds:F0} ms.")
        : Lang.Pick("Les AirPods ne sont pas venus. Boîtier fermé ou hors de portée ?", "The AirPods did not come. Case closed or out of range?"));
    return ok ? 0 : 2;
}

int Release()
{
    var ep = RequireEndpoint();
    if (BluetoothAudio.GetState(ep.Id) != HeadsetState.Connected)
        throw new UserError(Lang.Pick("Les AirPods ne sont pas sur ce PC.", "The AirPods are not on this PC."));

    var (ok, _) = SendAndWait(ep, BluetoothAudio.Disconnect, s => s != HeadsetState.Connected);
    // Le micro, s'il est activé, garderait les AirPods accrochés au PC.
    if (BluetoothAudio.ReleaseHandsFree(config.Mac!) is { } code && code != 0)
        Console.WriteLine(Lang.Pick($"Relâche du micro refusée (code Windows {code}) : l'iPhone pourrait ne pas reprendre les AirPods.",
                                    $"Microphone release refused (Windows code {code}): the iPhone may not take the AirPods back."));
    Console.WriteLine(ok ? Lang.Pick("AirPods rendus : l'iPhone va les reprendre.", "AirPods given back: the iPhone will take them back.")
                         : Lang.Pick("Les AirPods sont toujours connectés.", "The AirPods are still connected."));
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
        Console.WriteLine(Lang.Pick("Configuration illisible : elle va être remplacée.", "Unreadable configuration: it will be replaced."));
        return new AcawConfig();
    }
}

BtEndpoint RequireEndpoint()
{
    if (!config.IsConfigured)
        throw new UserError(Lang.Pick("Aucun AirPods configuré. Lance d'abord « acaw setup ».", "No AirPods configured. Run \"acaw setup\" first."));
    return BluetoothAudio.FindA2dp(config.Mac!)
        ?? throw new UserError(Lang.Pick($"{config.Name} n'est plus appairé à ce PC. Ré-appaire-le puis relance « acaw setup ».",
                                         $"{config.Name} is no longer paired with this PC. Pair it again, then run \"acaw setup\" again."));
}

string Describe(HeadsetState state) => state switch
{
    HeadsetState.Connected => Lang.Pick("connectés", "connected"),
    HeadsetState.Disconnected => Lang.Pick("non (ailleurs ou libres)", "no (elsewhere or idle)"),
    _ => bluetoothOff,
};

static string Pct(int? value) => value is null ? "—" : $"{value} %";

static int Fail(string message)
{
    Console.WriteLine(message);
    return 1;
}

/// <summary>Erreur d'utilisation : on affiche le message sans pile d'appels.</summary>
sealed class UserError(string message) : Exception(message);
