using System.Diagnostics;
using System.Globalization;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Media.Control;

namespace PodHop.Spike;

// Étape 0 de l'ADR-0004 (pause au retrait d'un écouteur) : la détection d'oreille par BLE est-elle assez
// rapide et assez stable pour mettre en pause ? Mode d'emploi : docs/guide/04-protocole-test-phase0.md, test D.

/// <summary>Une trame des AirPods réduite à ce qui compte ici : quand, qui l'émet, combien d'écouteurs portés.</summary>
internal readonly record struct EarSample(long Ticks, ulong Address, int InEar);

/// <summary>Enregistre le nombre d'écouteurs portés annoncé par chaque trame BLE du modèle demandé.</summary>
internal sealed class EarProbe : IDisposable
{
    private readonly Lock _gate = new();
    private readonly List<EarSample> _samples = [];
    private readonly BluetoothLEAdvertisementWatcher _watcher;

    public EarProbe(string model)
    {
        // L'heure est prise à la réception par le programme : quelques ms après la radio, négligeable ici.
        _watcher = AppleBle.Start(frame =>
        {
            if (frame.Model != model) return;
            var sample = new EarSample(Stopwatch.GetTimestamp(), frame.Address, frame.PodsInEar);
            lock (_gate) _samples.Add(sample);
        });
    }

    /// <summary>Dernier compte reçu, ou null si aucune trame depuis <paramref name="maxAge"/>.</summary>
    public int? Current(TimeSpan maxAge)
    {
        lock (_gate)
            return _samples.Count > 0 && Stopwatch.GetElapsedTime(_samples[^1].Ticks) <= maxAge
                ? _samples[^1].InEar
                : null;
    }

    public List<EarSample> Since(long ticks)
    {
        lock (_gate) return _samples.Where(s => s.Ticks >= ticks).ToList();
    }

    public void Dispose() => _watcher.Stop();
}

/// <summary>
/// Note chaque changement d'état des lecteurs média de Windows (SMTC). Répond à la question : les AirPods
/// envoient-ils eux-mêmes une pause à Windows quand on retire un écouteur ?
/// </summary>
internal sealed class MediaProbe : IDisposable
{
    public readonly record struct Change(long Ticks, string App, string Status);

    private readonly GlobalSystemMediaTransportControlsSessionManager _manager;
    private readonly Lock _gate = new();
    private readonly List<Change> _changes = [];
    private readonly Dictionary<string, string> _last = []; // dernier état connu de chaque appli
    private List<GlobalSystemMediaTransportControlsSession> _sessions = [];

    private MediaProbe(GlobalSystemMediaTransportControlsSessionManager manager)
    {
        _manager = manager;
        _manager.SessionsChanged += OnSessionsChanged;
        Refresh();
    }

    public static MediaProbe Create() =>
        new(GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask().GetAwaiter().GetResult());

    public string Describe()
    {
        lock (_gate)
            return _last.Count == 0 ? "aucun" : string.Join(", ", _last.Select(kv => $"{kv.Key} ({kv.Value})"));
    }

    public List<Change> Since(long ticks)
    {
        lock (_gate) return _changes.Where(c => c.Ticks >= ticks).ToList();
    }

    public void Dispose()
    {
        _manager.SessionsChanged -= OnSessionsChanged;
        lock (_gate)
            foreach (var session in _sessions) session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
    }

    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args) =>
        Refresh();

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession session, PlaybackInfoChangedEventArgs args)
    {
        lock (_gate) Note(session, record: true);
    }

    // Une appli ouverte ou fermée : on se réabonne à toutes les sessions. Leur état actuel n'est pas un changement.
    private void Refresh()
    {
        lock (_gate)
        {
            foreach (var session in _sessions) session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
            _sessions = [.. _manager.GetSessions()];
            foreach (var session in _sessions)
            {
                session.PlaybackInfoChanged += OnPlaybackInfoChanged;
                Note(session, record: false);
            }
        }
    }

    // PlaybackInfoChanged se déclenche pour bien d'autres raisons (position, piste…) : on ne garde que
    // les vrais changements d'état (lecture, pause…).
    private void Note(GlobalSystemMediaTransportControlsSession session, bool record)
    {
        string app, status;
        try
        {
            app = session.SourceAppUserModelId;
            status = session.GetPlaybackInfo()?.PlaybackStatus.ToString() ?? "?";
        }
        catch (Exception)
        {
            return; // l'appli vient de se fermer
        }
        if (_last.TryGetValue(app, out var before) && before == status) return;
        _last[app] = status;
        if (!record) return;
        _changes.Add(new Change(Stopwatch.GetTimestamp(), app, status));
        Console.WriteLine($"      [média] {app} → {status}");
    }
}

/// <summary>La commande « oreille » : gestes guidés et chronométrés, puis test de stabilité.</summary>
internal static class EarTest
{
    private static readonly TimeSpan Lead = TimeSpan.FromSeconds(2);    // le geste peut précéder un peu l'appui sur Entrée
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10); // au-delà, le geste n'a pas été vu
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(3);   // observation après le changement : retours en arrière ?
    private static readonly TimeSpan Fresh = TimeSpan.FromSeconds(10);   // âge maximal de la dernière trame pour connaître l'état
    private static readonly TimeSpan Criterion = TimeSpan.FromSeconds(3); // critère de l'étape 0 : p95 ≤ 3 s

    private static readonly (string Text, int From, int To)[] Gestures =
    [
        ("Retire un écouteur de l'oreille (garde-le en main)", 2, 1),
        ("Retire le second (garde-le en main)", 1, 0),
        ("Remets un écouteur", 0, 1),
        ("Remets le second", 1, 2),
    ];

    private sealed record Trial(
        string Gesture, bool Seen, double? FirstMs, double? ConfirmedMs,
        int StaleFrames, int Reversions, int Unexpected, double? MediaMs, string? MediaStatus);

    public static int Run(string model, int cycles, int freeSeconds, string label)
    {
        using var ear = new EarProbe(model);
        using var media = MediaProbe.Create();
        Console.WriteLine($"Lecteurs média vus par Windows : {media.Describe()}");
        Console.WriteLine("Tape « q » puis Entrée à n'importe quelle question pour passer à la suite.");
        Console.WriteLine();

        var trials = new List<Trial>();
        RunGestures(ear, media, cycles, label, trials);
        if (trials.Count > 0) Summarize(trials);
        if (freeSeconds > 0) Stability(ear, freeSeconds, label);
        return 0;
    }

    // Les quatre gestes, répétés : 2 → 1 → 0 → 1 → 2. S'arrête plus tôt si l'utilisateur tape « q ».
    private static void RunGestures(EarProbe ear, MediaProbe media, int cycles, string label, List<Trial> trials)
    {
        for (int c = 1; c <= cycles; c++)
        {
            foreach (var g in Gestures)
            {
                if (!AwaitState(ear, g.From)) return;
                Console.Write($"[{c}/{cycles}] {g.Text} ({g.From}→{g.To}) : appuie sur Entrée PENDANT le geste… ");
                if (!Answer()) return;
                long t0 = Stopwatch.GetTimestamp();
                var trial = Observe(ear, media, t0, g.From, g.To);
                Console.WriteLine($"   {Describe(trial)}");
                AppendCsv(label, c, trial);
                trials.Add(trial);
            }
        }
    }

    // Attend que les AirPods annoncent le nombre d'écouteurs portés voulu. false si l'utilisateur passe.
    private static bool AwaitState(EarProbe ear, int expected)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(3)) // laisser le temps à une trame d'arriver
        {
            if (ear.Current(Fresh) == expected) return true;
            Thread.Sleep(100);
        }
        while (true)
        {
            var now = ear.Current(Fresh);
            if (now == expected) return true;
            Console.Write($"   Les AirPods annoncent {(now is null ? "rien (boîtier fermé ?)" : $"{now} écouteur(s) porté(s)")} ; " +
                          $"il en faut {expected}. Ajuste, puis Entrée… ");
            if (!Answer()) return false;
            Thread.Sleep(1500); // laisser une trame arriver après l'ajustement
        }
    }

    // false si l'utilisateur tape « q », ou si l'entrée est fermée (ReadLine renvoie null).
    private static bool Answer() => Console.ReadLine()?.Trim() is { } line
                                    && !line.Equals("q", StringComparison.OrdinalIgnoreCase);

    private static Trial Observe(EarProbe ear, MediaProbe media, long t0, int from, int to)
    {
        long start = t0 - (long)(Lead.TotalSeconds * Stopwatch.Frequency);
        while (true)
        {
            Thread.Sleep(100);
            var match = ear.Since(start).FirstOrDefault(s => s.InEar == to);
            if (Stopwatch.GetElapsedTime(t0) >= Timeout) break;
            if (match != default && Stopwatch.GetElapsedTime(match.Ticks) >= Settle) break;
        }

        var samples = ear.Since(start);
        double Ms(long ticks) => Stopwatch.GetElapsedTime(t0, ticks).TotalMilliseconds;
        int unexpected = samples.Count(s => s.InEar != from && s.InEar != to);
        var firstMedia = media.Since(start).FirstOrDefault();
        double? mediaMs = firstMedia == default ? null : Ms(firstMedia.Ticks);
        string gesture = $"{from}→{to}";

        int i = samples.FindIndex(s => s.InEar == to);
        if (i < 0)
            return new Trial(gesture, false, null, null, samples.Count(s => s.Ticks >= t0), 0, unexpected,
                             mediaMs, firstMedia.Status);

        // « Confirmé » = deux trames de suite (toutes adresses confondues) avec la nouvelle valeur :
        // c'est ce que coûterait un filtre anti-rebond de deux trames dans ACAW.
        int j = Enumerable.Range(i + 1, samples.Count - i - 1)
                          .FirstOrDefault(k => samples[k].InEar == to && samples[k - 1].InEar == to, -1);
        return new Trial(
            gesture, true, Ms(samples[i].Ticks), j < 0 ? null : Ms(samples[j].Ticks),
            StaleFrames: samples.Count(s => s.Ticks >= t0 && s.Ticks < samples[i].Ticks),
            Reversions: samples.Skip(i + 1).Count(s => s.InEar != to),
            unexpected, mediaMs, firstMedia.Status);
    }

    private static string Describe(Trial t)
    {
        if (!t.Seen) return $"NON VU en {Timeout.TotalSeconds:F0} s ({t.StaleFrames} trames reçues sans changement)";
        string text = $"1re trame {F(t.FirstMs)} · confirmé {F(t.ConfirmedMs)} · trames périmées {t.StaleFrames} · retours {t.Reversions}";
        if (t.Unexpected > 0) text += $" · valeurs inattendues {t.Unexpected}";
        if (t.MediaMs is not null) text += $" · Windows : {t.MediaStatus} à {F(t.MediaMs)}";
        return text;
    }

    private static void Summarize(List<Trial> trials)
    {
        Console.WriteLine();
        Console.WriteLine("Résumé par geste (délais comptés depuis l'appui sur Entrée) :");
        foreach (var group in trials.GroupBy(t => t.Gesture))
        {
            var seen = group.Where(t => t.Seen).ToList();
            Console.WriteLine($"  {group.Key}  vus {seen.Count}/{group.Count()}" +
                              $" · 1re trame {Stats(seen.Select(t => t.FirstMs))}" +
                              $" · confirmé {Stats(seen.Select(t => t.ConfirmedMs))}" +
                              $" · retours {group.Sum(t => t.Reversions)}" +
                              $" · inattendues {group.Sum(t => t.Unexpected)}" +
                              $" · réactions de Windows {group.Count(t => t.MediaMs is not null)}");
        }

        // Critères de l'étape 0 (ADR-0004) : retraits vus vite, et aucune valeur parasite.
        var removals = trials.Where(t => t.Gesture is "2→1" or "1→0").ToList();
        var firsts = removals.Select(t => t.FirstMs ?? double.MaxValue).Order().ToList();
        bool fast = firsts.Count > 0 && Percentile(firsts, 95) <= Criterion.TotalMilliseconds;
        bool clean = trials.All(t => t.Reversions == 0 && t.Unexpected == 0);
        Console.WriteLine();
        Console.WriteLine($"Critère « retrait vu en ≤ {Criterion.TotalSeconds:F0} s (p95) » : {(fast ? "OK" : "NON")}");
        Console.WriteLine($"Critère « aucune valeur parasite » : {(clean ? "OK" : "NON")}");
        Console.WriteLine("Mesures ajoutées à mesures-oreille.csv");
    }

    // Les deux écouteurs portés, sans y toucher : le compte doit rester à 2. Toute autre valeur serait
    // une pause intempestive dans ACAW.
    private static void Stability(EarProbe ear, int seconds, string label)
    {
        Console.WriteLine();
        if (!AwaitState(ear, 2)) return;
        Console.Write($"Stabilité : {seconds} s avec les deux écouteurs, sans les toucher. Bouge, parle, tourne la tête, " +
                      "mâche… Entrée pour commencer (Ctrl+C arrête plus tôt). ");
        if (!Answer()) return;

        long t0 = Stopwatch.GetTimestamp();
        using var done = new ManualResetEventSlim();
        ConsoleCancelEventHandler stop = (_, e) => { e.Cancel = true; done.Set(); };
        Console.CancelKeyPress += stop;
        done.Wait(TimeSpan.FromSeconds(seconds));
        Console.CancelKeyPress -= stop;
        long end = Stopwatch.GetTimestamp();
        var samples = ear.Since(t0).Where(s => s.Ticks <= end).ToList();

        double Sec(long ticks) => Stopwatch.GetElapsedTime(t0, ticks).TotalSeconds;
        double maxGap = samples.Zip(samples.Skip(1), (a, b) => Sec(b.Ticks) - Sec(a.Ticks)).DefaultIfEmpty(0).Max();

        // Une anomalie = une suite de trames qui n'annoncent pas 2 écouteurs portés.
        var anomalies = new List<(double At, int Value, int Frames, double Seconds)>();
        for (int i = 0; i < samples.Count; i++)
        {
            if (samples[i].InEar == 2) continue;
            int k = i;
            while (k + 1 < samples.Count && samples[k + 1].InEar != 2) k++;
            long until = k + 1 < samples.Count ? samples[k + 1].Ticks : end;
            anomalies.Add((Sec(samples[i].Ticks), samples[i].InEar, k - i + 1, Sec(until) - Sec(samples[i].Ticks)));
            i = k;
        }

        double duration = Sec(end);
        Console.WriteLine();
        Console.WriteLine($"Stabilité sur {duration:F0} s : {samples.Count} trames, écart max {maxGap:F1} s, " +
                          $"anomalies {anomalies.Count}");
        foreach (var a in anomalies)
            Console.WriteLine($"  à {a.At:F1} s : {a.Value} écouteur(s) porté(s), {a.Frames} trame(s), {a.Seconds:F1} s");
        Console.WriteLine($"Critère « aucune anomalie » : {(anomalies.Count == 0 ? "OK" : "NON")}");

        const string path = "mesures-oreille-stabilite.csv";
        if (!File.Exists(path)) File.WriteAllText(path, "date;label;secondes;trames;ecart_max_s;anomalies;trames_anormales;anomalie_max_s\n");
        File.AppendAllText(path, string.Join(';',
            DateTime.Now.ToString("s", CultureInfo.InvariantCulture), label, N(duration), samples.Count, N(maxGap, "F1"),
            anomalies.Count, anomalies.Sum(a => a.Frames), N(anomalies.Select(a => a.Seconds).DefaultIfEmpty(0).Max(), "F1")) + "\n");
        Console.WriteLine($"Mesure ajoutée à {path}");
    }

    private static void AppendCsv(string label, int cycle, Trial t)
    {
        const string path = "mesures-oreille.csv";
        if (!File.Exists(path))
            File.WriteAllText(path, "date;label;cycle;geste;vu;premiere_ms;confirmee_ms;trames_perimees;retours;inattendues;windows_ms;windows_etat\n");
        File.AppendAllText(path, string.Join(';',
            DateTime.Now.ToString("s", CultureInfo.InvariantCulture), label, cycle, t.Gesture, t.Seen ? 1 : 0,
            N(t.FirstMs), N(t.ConfirmedMs), t.StaleFrames, t.Reversions, t.Unexpected, N(t.MediaMs), t.MediaStatus ?? "") + "\n");
    }

    private static string Stats(IEnumerable<double?> values)
    {
        var sorted = values.Where(v => v is not null).Select(v => v!.Value).Order().ToList();
        return sorted.Count == 0
            ? "—"
            : $"médiane {Percentile(sorted, 50):F0} ms, p95 {Percentile(sorted, 95):F0} ms, max {sorted[^1]:F0} ms";
    }

    // Méthode « nearest rank », comme pour le bench.
    private static double Percentile(List<double> sorted, int p) =>
        sorted[Math.Clamp((int)Math.Ceiling(p / 100.0 * sorted.Count) - 1, 0, sorted.Count - 1)];

    private static string F(double? ms) => ms is null ? "—" : $"{ms:F0} ms";

    private static string N(double? value, string format = "F0") => value?.ToString(format, CultureInfo.InvariantCulture) ?? "";
}
