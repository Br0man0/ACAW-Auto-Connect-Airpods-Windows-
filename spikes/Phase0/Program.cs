// Spike de la phase 0 : code jetable qui répond à trois questions avant d'écrire le vrai projet.
//   Test A : la reconnexion KS prend-elle les AirPods à l'iPhone, et en combien de temps ?  → connect / bench
//   Test B : l'iPhone les reprend-il ensuite tout seul ?                                    → connect puis observation
//   Test C : que disent les annonces BLE (boîtier ouvert, dans l'oreille) ?                  → ble, bench --ble
//   Test D (ADR-0004) : le retrait d'un écouteur se voit-il vite et sans erreur ?             → oreille
// Mode d'emploi : docs/guide/04-protocole-test-phase0.md

using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using PodHop.Spike;
using Windows.Win32.Media.Audio;
using Windows.Win32.Media.KernelStreaming;

Console.OutputEncoding = Encoding.UTF8;

const string Usage = """
    Utilisation : phase0 <commande> [options]

      list                                   liste les casques Bluetooth connus de Windows
      connect <filtre>                       connecte le casque et chronomètre
      disconnect <filtre>                    déconnecte le casque
      bench <filtre> [--n 10] [--manuel] [--ble] [--label texte] [--repos 5]
                                             répète connect, puis affiche médiane / p95
      ble [--secondes 120] [--modele 2027]   affiche les annonces BLE des AirPods à proximité,
                                             puis le rythme d'émission de chaque adresse
      oreille --modele 2027 [--n 10] [--libre 600] [--label texte]
                                             chronomètre le retrait et la remise des écouteurs (n cycles),
                                             puis vérifie la stabilité du compte pendant --libre secondes

      <filtre> : une partie du nom (ex. « AirPods ») ou l'adresse MAC.
    """;

if (args.Length == 0)
{
    Console.WriteLine(Usage);
    return 1;
}

try
{
    return args[0] switch
    {
        "list" => List(),
        "connect" => Connect(Find(Arg(1)), log: true) is not null ? 0 : 2,
        "disconnect" => Disconnect(Find(Arg(1))) ? 0 : 2,
        "bench" => Bench(Find(Arg(1))),
        "ble" => Ble(),
        "oreille" => EarTest.Run(
            Option("--modele")?.ToUpperInvariant()
                ?? throw new UserError("Précise le modèle des AirPods : --modele 2027 (le « modèle » affiché par list)."),
            int.Parse(Option("--n") ?? "10", CultureInfo.InvariantCulture),
            int.Parse(Option("--libre") ?? "0", CultureInfo.InvariantCulture),
            Option("--label") ?? "sans-label"),
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
    Console.WriteLine("Le Bluetooth du PC est indisponible (ERROR_DEVICE_NOT_AVAILABLE). " +
                      "Active-le dans Paramètres → Bluetooth et appareils, puis relance.");
    return 3;
}
catch (COMException e)
{
    Console.WriteLine($"Erreur Windows 0x{e.HResult:X8} : {e.Message}");
    return 3;
}

int List()
{
    var endpoints = BluetoothAudio.ListEndpoints();
    if (endpoints.Count == 0) Console.WriteLine("Aucun casque Bluetooth trouvé. Les AirPods sont-ils appairés ?");
    foreach (var ep in endpoints)
    {
        Console.WriteLine($"{ep.Name}");
        Console.WriteLine($"   profil={(ep.IsA2dp ? "A2DP (musique)" : "HFP (mains libres)")}  mac={ep.Mac ?? "?"}  " +
                          $"modèle={ep.ModelId ?? "?"}  état={BluetoothAudio.GetState(ep.Id)}");
        Console.WriteLine($"   filtre KS : {ep.KsFilterId}");
    }
    return 0;
}

// Renvoie le temps jusqu'à l'état ACTIVE, ou null en cas d'échec.
TimeSpan? Connect(BtEndpoint ep, bool log)
{
    var state = BluetoothAudio.GetState(ep.Id);
    if (state == DEVICE_STATE.DEVICE_STATE_ACTIVE)
    {
        Console.WriteLine("Déjà connecté sur ce PC. Déconnecte d'abord (ou mets les AirPods sur l'iPhone).");
        return null;
    }
    if (state == DEVICE_STATE.DEVICE_STATE_NOTPRESENT)
    {
        Console.WriteLine("Casque absent pour Windows : le Bluetooth du PC est probablement coupé.");
        return null;
    }

    var clock = Stopwatch.StartNew();
    BluetoothAudio.SendOneShot(ep, KSPROPERTY_BTAUDIO.KSPROPERTY_ONESHOT_RECONNECT);
    var callTime = clock.Elapsed;
    // Large exprès : le spike doit mesurer les connexions lentes, pas les compter comme des échecs.
    bool ok = WaitFor(ep, s => s == DEVICE_STATE.DEVICE_STATE_ACTIVE, TimeSpan.FromSeconds(30));
    var total = clock.Elapsed;

    if (log)
        Console.WriteLine(ok
            ? $"Connecté en {Ms(total)} (appel Windows : {Ms(callTime)})"
            : $"Échec : pas connecté après {Ms(total)}. Boîtier fermé ? Hors de portée ?");
    return ok ? total : null;
}

bool Disconnect(BtEndpoint ep)
{
    var clock = Stopwatch.StartNew();
    BluetoothAudio.SendOneShot(ep, KSPROPERTY_BTAUDIO.KSPROPERTY_ONESHOT_DISCONNECT);
    bool ok = WaitFor(ep, s => s != DEVICE_STATE.DEVICE_STATE_ACTIVE, TimeSpan.FromSeconds(10));
    Console.WriteLine(ok ? $"Déconnecté en {Ms(clock.Elapsed)}" : "Toujours connecté après 10 s.");
    return ok;
}

int Bench(BtEndpoint ep)
{
    int n = int.Parse(Option("--n") ?? "10", CultureInfo.InvariantCulture);
    int rest = int.Parse(Option("--repos") ?? "5", CultureInfo.InvariantCulture);
    bool manual = Flag("--manuel");
    string label = Option("--label") ?? (manual ? "manuel" : "libre");
    using var ble = Flag("--ble") ? new BleScope() : null;

    var times = new List<double>();
    for (int i = 1; i <= n; i++)
    {
        if (manual)
        {
            Console.Write($"[{i}/{n}] Mets les AirPods sur l'autre appareil, puis Entrée… ");
            Console.ReadLine();
            while (BluetoothAudio.GetState(ep.Id) == DEVICE_STATE.DEVICE_STATE_ACTIVE)
            {
                Console.Write("   Ils sont encore connectés à ce PC. Remets-les sur l'autre appareil, puis Entrée… ");
                Console.ReadLine();
            }
        }
        else if (BluetoothAudio.GetState(ep.Id) == DEVICE_STATE.DEVICE_STATE_ACTIVE)
        {
            Disconnect(ep);
            Thread.Sleep(TimeSpan.FromSeconds(rest)); // laisser la radio et les AirPods se stabiliser
        }

        // Un essai qui n'a pas pu démarrer n'est pas un échec de connexion : on ne l'enregistre pas.
        var state = BluetoothAudio.GetState(ep.Id);
        if (state == DEVICE_STATE.DEVICE_STATE_NOTPRESENT)
            throw new UserError("Casque absent pour Windows : le Bluetooth du PC est probablement coupé.");
        if (state == DEVICE_STATE.DEVICE_STATE_ACTIVE)
        {
            Console.WriteLine($"[{i}/{n}] ignoré : déjà connecté à ce PC");
            continue;
        }

        var t = Connect(ep, log: false);
        Console.WriteLine($"[{i}/{n}] {(t is null ? "ÉCHEC" : Ms(t.Value))}");
        AppendCsv(label, ble is not null, i, t);
        if (t is not null) times.Add(t.Value.TotalMilliseconds);
    }

    Console.WriteLine();
    Console.WriteLine($"Réussites : {times.Count}/{n}");
    if (times.Count > 0)
    {
        times.Sort();
        Console.WriteLine($"min {times[0]:F0} ms · médiane {Percentile(times, 50):F0} ms · " +
                          $"p95 {Percentile(times, 95):F0} ms · max {times[^1]:F0} ms");
    }
    Console.WriteLine("Mesures ajoutées à mesures-phase0.csv");
    return 0;
}

int Ble()
{
    int seconds = int.Parse(Option("--secondes") ?? "120", CultureInfo.InvariantCulture);
    Console.WriteLine($"Écoute BLE pendant {seconds} s (Ctrl+C pour arrêter). Une ligne = un changement.");
    Console.WriteLine("Fais les gestes demandés par le protocole (docs/guide/04-protocole-test-phase0.md).");
    // Le modèle filtre les autres appareils Apple, qui émettent aussi des trames de type 0x07.
    string? model = Option("--modele")?.ToUpperInvariant();
    var lastSeen = new HashSet<string>();
    var rhythm = new Dictionary<ulong, (string Model, int Count, DateTime Last, TimeSpan MaxGap)>();
    // Les deux écouteurs émettent chacun de leur côté : ce qui compte pour savoir si la paire est
    // éveillée, c'est l'écart entre deux trames quelconques de la paire.
    (int Count, DateTime Last, TimeSpan MaxGap) pair = (0, DateTime.MinValue, TimeSpan.Zero);
    var watcher = AppleBle.Start(frame =>
    {
        if (model is not null && frame.Model != model) return;
        lock (lastSeen)
        {
            var now = DateTime.Now;
            rhythm[frame.Address] = rhythm.TryGetValue(frame.Address, out var r)
                ? (r.Model, r.Count + 1, now, now - r.Last > r.MaxGap ? now - r.Last : r.MaxGap)
                : (frame.Model, 1, now, TimeSpan.Zero);
            pair = pair.Count == 0
                ? (1, now, TimeSpan.Zero)
                : (pair.Count + 1, now, now - pair.Last > pair.MaxGap ? now - pair.Last : pair.MaxGap);
            if (lastSeen.Add(frame.ClearPartKey)) Console.WriteLine(frame);
        }
    });

    using var done = new ManualResetEventSlim();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; done.Set(); };
    done.Wait(TimeSpan.FromSeconds(seconds));
    watcher.Stop();

    // Le plus grand écart entre deux trames d'une même adresse dit combien de temps attendre
    // avant de conclure « les AirPods se sont endormis » (boîtier fermé).
    Console.WriteLine();
    Console.WriteLine("Rythme d'émission par adresse :");
    lock (lastSeen)
        foreach (var (address, r) in rhythm.OrderByDescending(kv => kv.Value.Count))
            Console.WriteLine($"  ble={address:X12}  modèle={r.Model}  trames={r.Count}  " +
                              $"écart max={r.MaxGap.TotalSeconds:F1} s");
    if (model is not null)
        Console.WriteLine($"  toutes adresses confondues  trames={pair.Count}  écart max={pair.MaxGap.TotalSeconds:F1} s");
    return 0;
}

// Attend que l'état de l'endpoint vérifie la condition, en le relisant toutes les 25 ms.
static bool WaitFor(BtEndpoint ep, Func<DEVICE_STATE, bool> condition, TimeSpan timeout)
{
    var clock = Stopwatch.StartNew();
    while (clock.Elapsed < timeout)
    {
        if (condition(BluetoothAudio.GetState(ep.Id))) return true;
        Thread.Sleep(25);
    }
    return false;
}

BtEndpoint Find(string filter)
{
    string compact = filter.Replace(":", "").Replace("-", "");
    var matches = BluetoothAudio.ListEndpoints()
        .Where(ep => ep.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                  || (ep.Mac?.Replace(":", "").Equals(compact, StringComparison.OrdinalIgnoreCase) ?? false))
        .ToList();

    // Un casque expose souvent deux endpoints : on vise celui de la musique (A2DP), jamais le mains libres.
    var a2dp = matches.Where(ep => ep.IsA2dp).ToList();
    if (a2dp.Count == 1) return a2dp[0];
    if (matches.Count == 1) return matches[0];
    if (matches.Count == 0) throw new UserError($"Aucun casque ne correspond à « {filter} ». Lance « phase0 list ».");
    throw new UserError($"Plusieurs casques correspondent à « {filter} » : " +
                        string.Join(", ", matches.Select(m => m.Name)) + ". Précise le filtre ou utilise la MAC.");
}

static void AppendCsv(string label, bool bleOn, int run, TimeSpan? t)
{
    const string path = "mesures-phase0.csv";
    if (!File.Exists(path)) File.WriteAllText(path, "date;scenario;ble;essai;ms\n");
    string ms = t is null ? "echec" : t.Value.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture);
    File.AppendAllText(path, $"{DateTime.Now:s};{label};{(bleOn ? 1 : 0)};{run};{ms}\n");
}

// Méthode « nearest rank » : valeur sous laquelle se trouvent p % des mesures.
static double Percentile(List<double> sorted, int p) =>
    sorted[Math.Clamp((int)Math.Ceiling(p / 100.0 * sorted.Count) - 1, 0, sorted.Count - 1)];

static string Ms(TimeSpan t) => $"{t.TotalMilliseconds:F0} ms";

string Arg(int index) => args.Length > index ? args[index] : throw new UserError(Usage);
string? Option(string name) => Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;
bool Flag(string name) => args.Contains(name);
static int Fail(string message) { Console.WriteLine(message); return 1; }

/// <summary>Erreur d'utilisation : on affiche le message sans pile d'appels.</summary>
sealed class UserError(string message) : Exception(message);

/// <summary>Garde l'écoute BLE active pendant un bench, pour mesurer son effet sur la connexion (risque R4).</summary>
sealed class BleScope : IDisposable
{
    private readonly Windows.Devices.Bluetooth.Advertisement.BluetoothLEAdvertisementWatcher _watcher =
        AppleBle.Start(_ => { });

    public void Dispose() => _watcher.Stop();
}
