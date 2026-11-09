using System.Diagnostics;
using Acaw.Core;
using Acaw.Core.Config;
using Acaw.Core.Hosting;
using Acaw.Core.Switching;
using Acaw.Windows;
using Acaw.Windows.Audio;
using Acaw.Windows.Ble;
using Acaw.Windows.Bluetooth;
using Microsoft.Win32;
using Timer = System.Windows.Forms.Timer;

namespace Acaw.Tray;

/// <summary>
/// L'application de la barre des tâches : une icône et un menu. Ce fichier ne contient aucune règle : la
/// bascule est décidée par l'hôte, et la configuration (choix des AirPods, relecture de config.json) par le
/// <see cref="ConfigCoordinator"/>. Ici, on affiche l'état et on transmet les clics.
/// Tout se passe sur le thread de l'interface ; ce qui arrive d'ailleurs (hôte, fichier) y est ramené.
/// </summary>
internal sealed class TrayApp : ApplicationContext
{
    // Tant qu'ACAW ne tourne pas (pas d'AirPods trouvés, service Windows pas prêt), il réessaie à ce rythme.
    private const int RetryIntervalMs = 30_000;

    // Fichier verrouillé par un éditeur pendant la lecture : nouvel essai peu après, mais pas indéfiniment
    // (un fichier verrouillé en permanence inonderait le journal). La prochaine écriture relancera la relecture.
    private const int LockedRetryMs = 1_000;
    private const int MaxLockedRetries = 10;

    private readonly NotifyIcon _icon = new() { Visible = true };
    private readonly ContextMenuStrip _menu = new();
    private readonly ToolStripMenuItem _status = new() { Enabled = false };
    private readonly ToolStripMenuItem _take = new("Amener sur ce PC");
    private readonly ToolStripMenuItem _release = new("Rendre à l'iPhone");
    private readonly ToolStripMenuItem _airPods = new("AirPods");
    private readonly ToolStripMenuItem _auto = new("Bascule automatique") { CheckOnClick = true };
    private readonly ToolStripMenuItem _startup = new("Démarrer avec Windows") { CheckOnClick = true };
    private const string MicText = "Micro des AirPods (appels)";
    private readonly ToolStripMenuItem _mic = new(MicText);
    private bool _micBusy;               // activation ou retrait du micro en cours (~3 s)
    private Action? _balloonAction;      // ce que fait un clic sur la notification affichée
    private readonly Control _ui = new(); // sert à revenir sur le thread de l'interface depuis l'hôte ou le fichier
    private readonly StartupShortcut _startupShortcut = StartupShortcut.ForCurrentUser(Application.ExecutablePath);
    private readonly Timer _retry = new() { Interval = RetryIntervalMs };
    private readonly Timer _lockedRetry = new() { Interval = LockedRetryMs };
    private readonly ConfigCoordinator _coordinator;
    private readonly ConfigWatcher? _watcher;
    private readonly EventWaitHandle _quit;
    private readonly RegisteredWaitHandle _quitWait;
    private AcawHost? _subscribedHost; // l'hôte dont on écoute les changements d'état
    private int _lockedRetries;         // essais successifs sur un fichier verrouillé
    private readonly HashSet<string> _handsFreeChecked = new(StringComparer.OrdinalIgnoreCase); // paires déjà vérifiées

    public TrayApp()
    {
        _ui.CreateControl();
        BuildMenu();

        _coordinator = new ConfigCoordinator(new FileConfigSource(ConfigStore.DefaultPath), new WindowsHeadsetCatalog(),
                                             new AdaptersFactory(), FileLog.Write);
        _coordinator.Changed += OnCoordinatorChanged;
        _coordinator.Notice += message => ShowBalloon("ACAW", message, ToolTipIcon.Info);
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        try
        {
            _watcher = new ConfigWatcher(ConfigStore.DefaultPath);
            _watcher.Changed += () => _ui.BeginInvoke(RefreshConfig);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Sans surveillance, ACAW marche quand même : il faudra le relancer après une modification à la main.
            FileLog.Write($"Surveillance de config.json impossible : {e.Message}");
        }

        // « acaw quit » (l'installateur) demande un arrêt propre : comme le menu « Quitter ».
        _quit = new QuitSignal().Listen();
        _quitWait = ThreadPool.RegisterWaitForSingleObject(_quit, (_, _) => _ui.BeginInvoke(ExitThread),
                                                           null, Timeout.Infinite, executeOnlyOnce: true);

        _retry.Tick += (_, _) => { if (_coordinator.Host is null) RefreshConfig(); };
        _lockedRetry.Tick += (_, _) => { _lockedRetry.Stop(); RefreshConfig(); };
        _retry.Start();

        FileLog.Write("Lecture de la configuration…");
        RefreshConfig();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged; // événement statique : à retirer sinon fuite
            _quitWait.Unregister(null);
            _quit.Dispose();
            _watcher?.Dispose(); // d'abord : plus aucune relecture ne doit arriver pendant l'arrêt
            _retry.Dispose();
            _lockedRetry.Dispose();
            _coordinator.Dispose();
            _icon.Visible = false;
            _icon.Dispose();
            _menu.Dispose();
            _ui.Dispose();
        }
        base.Dispose(disposing);
    }

    private void BuildMenu()
    {
        _take.Click += (_, _) => _coordinator.Host?.Request(new TakeRequested());
        _release.Click += (_, _) => _coordinator.Host?.Request(new ReleaseRequested());
        // Click et non CheckedChanged : cocher la case par le code (à l'affichage) ne doit rien déclencher.
        _auto.Click += (_, _) => SetAutoSwitch(_auto.Checked);
        _startup.Click += (_, _) => SetStartup(_startup.Checked);
        _airPods.DropDownItems.Add("…"); // sans élément, Windows n'affiche pas la flèche du sous-menu
        _airPods.DropDownOpening += (_, _) => FillAirPodsMenu();
        _mic.Click += (_, _) => ToggleMicrophone(!_mic.Checked);
        // Pas d'effacement à la fermeture : sous Windows 11, une bulle expirée reste dans le centre de
        // notifications (Win + N) et peut être cliquée plus tard. Seule la notification suivante la remplace.
        _icon.BalloonTipClicked += (_, _) =>
        {
            var action = _balloonAction;
            _balloonAction = null;
            if (action is not null) Safely(action);
        };

        _menu.Items.AddRange(
        [
            _status,
            new ToolStripSeparator(),
            _take,
            _release,
            new ToolStripSeparator(),
            _airPods,
            _mic,
            _auto,
            _startup,
            new ToolStripMenuItem("Ouvrir le journal", null, (_, _) => OpenLog()),
            new ToolStripSeparator(),
            new ToolStripMenuItem($"ACAW {AppInfo.Version}") { Enabled = false },
            new ToolStripMenuItem("Quitter", null, (_, _) => ExitThread()),
        ]);
        _menu.Opening += (_, _) =>
        {
            _startup.Checked = _startupShortcut.IsEnabled;
            if (_coordinator.Host is null) RefreshConfig(); // les AirPods viennent peut-être d'être appairés
            ShowMicrophoneState();
        };

        _icon.ContextMenuStrip = _menu;
        _icon.Icon = TrayIcons.For(SwitchState.Away, autoSwitch: false);
        _icon.Text = "ACAW";
        _status.Text = "Démarrage…";
    }

    private void RefreshConfig() => Safely(() =>
    {
        if (_coordinator.Refresh()) _lockedRetries = 0;
        else if (++_lockedRetries <= MaxLockedRetries) _lockedRetry.Start();
        else if (_lockedRetries == MaxLockedRetries + 1) FileLog.Write("config.json reste verrouillé : relu à la prochaine modification");
    });

    // Le sous-menu « AirPods » est rempli à chaque ouverture : un casque a pu être appairé entre-temps.
    private void FillAirPodsMenu() => Safely(() =>
    {
        _airPods.DropDownItems.Clear();
        var candidates = _coordinator.Candidates();
        if (candidates.Count == 0)
            _airPods.DropDownItems.Add(new ToolStripMenuItem("Aucun AirPods appairé à ce PC") { Enabled = false });
        foreach (var headset in candidates)
        {
            var item = new ToolStripMenuItem(headset.Name)
            {
                Checked = AirPodsPicker.SameMac(headset.Mac, _coordinator.Config?.Mac),
            };
            item.Click += (_, _) => Safely(() => _coordinator.Choose(headset));
            _airPods.DropDownItems.Add(item);
        }
        _airPods.DropDownItems.Add(new ToolStripSeparator());
        _airPods.DropDownItems.Add(new ToolStripMenuItem("Paramètres Bluetooth…", null, (_, _) =>
            Process.Start(new ProcessStartInfo("ms-settings:bluetooth") { UseShellExecute = true })));
    });

    // Nouvel hôte, ou plus d'hôte : on écoute le bon, et on met l'affichage à jour.
    private void OnCoordinatorChanged()
    {
        var host = _coordinator.Host;
        if (host != _subscribedHost)
        {
            if (_subscribedHost is not null) _subscribedHost.StateChanged -= OnHostStateChanged;
            if (host is not null) host.StateChanged += OnHostStateChanged;
            _subscribedHost = host;
            if (host is not null)
            {
                FileLog.Write($"ACAW démarré pour {_coordinator.Config!.Name}");
                WarnIfHandsFree(_coordinator.Config.Mac!);
            }
        }
        Render();
    }

    // Profil mains libres actif = bascules de 10 s au lieu de 2 (ADR-0001). Une bulle par session et par paire.
    private void WarnIfHandsFree(string mac)
    {
        if (!_handsFreeChecked.Add(mac)) return;
        bool active;
        try
        {
            active = HandsFreeProfile.IsEnabled(mac);
        }
        catch (Exception e)
        {
            FileLog.Write($"Vérification du profil mains libres impossible : {e.Message}");
            return;
        }
        if (!active) return;
        FileLog.Write("Profil mains libres actif : chaque bascule prendra ~10 s au lieu de ~2 s");
        if (_coordinator.Config?.Microphone == true) return; // activé exprès depuis ACAW : pas d'avertissement
        ShowBalloon("ACAW : bascules lentes",
            "Le micro des AirPods (profil « mains libres ») est actif : chaque bascule prend ~10 s au lieu de 2. " +
            "Clique ici pour le retirer, ou garde-le pour les appels (menu → Micro des AirPods).",
            ToolTipIcon.Warning, onClick: () => ToggleMicrophone(on: false));
    }

    // La case reflète l'état réel du profil (il a pu être changé dans les propriétés Bluetooth de Windows).
    private void ShowMicrophoneState()
    {
        if (_micBusy) return;
        string? mac = _coordinator.Host is null ? null : _coordinator.Config?.Mac;
        _mic.Enabled = mac is not null;
        try
        {
            _mic.Checked = mac is not null && HandsFreeProfile.IsEnabled(mac);
        }
        catch (Exception e)
        {
            FileLog.Write($"Lecture du profil mains libres impossible : {e.Message}");
        }
    }

    // Active ou retire le micro (~3 s, hors du thread de l'interface). L'hôte est prévenu : les coupures du lien
    // que Windows provoque pendant ce temps ne sont pas une perte des AirPods (pas de pause, pas de relâche).
    private void ToggleMicrophone(bool on)
    {
        if (_micBusy || _coordinator.Config?.Mac is not { } mac) return;
        var host = _coordinator.Host;
        _micBusy = true;
        _mic.Enabled = false;
        _mic.Text = on ? "Micro des AirPods : activation…" : "Micro des AirPods : retrait…";
        FileLog.Write(on ? "Micro des AirPods : activation demandée" : "Micro des AirPods : retrait demandé");
        host?.BeginProfileChange();

        Task.Run(() => HandsFreeProfile.SetEnabled(mac, on)).ContinueWith(task => _ui.BeginInvoke(() =>
        {
            host?.EndProfileChange();
            _micBusy = false;
            _mic.Text = MicText;
            _mic.Enabled = true;
            if (task.Exception is { } e)
            {
                FileLog.Write($"Micro des AirPods : erreur {e.InnerException}");
                ShowBalloon("ACAW", "Changement du micro impossible (détail dans le journal).", ToolTipIcon.Warning);
                return;
            }
            var outcome = task.Result;
            FileLog.Write($"Micro des AirPods : {outcome.Result} (code Windows {outcome.Code})");
            if (outcome.Succeeded) Safely(() => _coordinator.SetMicrophone(on));
            ShowBalloon("ACAW", HandsFreeProfile.Describe(outcome.Result, on),
                        outcome.Succeeded ? ToolTipIcon.Info : ToolTipIcon.Warning);
        }));
    }

    // Une seule notification à la fois : la suivante remplace la précédente, et son action de clic avec elle.
    private void ShowBalloon(string title, string text, ToolTipIcon icon, Action? onClick = null)
    {
        _balloonAction = onClick;
        _icon.ShowBalloonTip(10_000, title, text, icon);
    }

    // Levé sur le thread de l'hôte. On ne transmet pas l'état reçu : Render relit celui de l'hôte ACTUEL, pour
    // qu'un message en retard de l'ancien hôte (configuration changée entre-temps) n'affiche pas un état faux.
    private void OnHostStateChanged(SwitchState _) => _ui.BeginInvoke(Render);

    private void Render()
    {
        var host = _coordinator.Host;
        if (host is null)
        {
            // ACAW ne peut pas fonctionner : on garde l'icône (AirPods, journal, Quitter) et on dit pourquoi.
            string why = _coordinator.Problem ?? "Non configuré";
            _status.Text = why;
            _icon.Text = why.Length <= 127 ? why : why[..127];
            _icon.Icon = TrayIcons.For(SwitchState.Away, autoSwitch: false);
            _take.Enabled = _release.Enabled = _auto.Enabled = false;
            return;
        }

        var state = host.State;
        bool auto = _coordinator.Config?.AutoSwitch ?? true; // à jour tout de suite, contrairement à l'hôte (sa file)
        string text = TrayIcons.Describe(state, auto);
        _status.Text = text;
        _icon.Text = text.Length <= 127 ? text : text[..127]; // limite de Windows pour l'info-bulle
        _icon.Icon = TrayIcons.For(state, auto);
        _auto.Enabled = true;
        _auto.Checked = auto;
        _take.Enabled = state is SwitchState.Away or SwitchState.Lost or SwitchState.Releasing;
        _release.Enabled = state is SwitchState.OnWindows or SwitchState.Switching;
    }

    private void SetAutoSwitch(bool on)
    {
        _coordinator.SetAutoSwitch(on);
        Render();
    }

    // Erreurs attendues en touchant au dossier Démarrage ou au registre : droits, disque, ou COM (le raccourci).
    private static bool IsStartupFailure(Exception e) =>
        e is UnauthorizedAccessException or System.Security.SecurityException or IOException
          or System.Runtime.InteropServices.COMException or InvalidCastException;

    private void SetStartup(bool enabled)
    {
        try
        {
            _startupShortcut.Set(enabled);
        }
        catch (Exception e) when (IsStartupFailure(e))
        {
            _startup.Checked = _startupShortcut.IsEnabled;
            ShowBalloon("ACAW", $"Réglage impossible : {e.Message}", ToolTipIcon.Warning);
        }
    }

    // Levé sur un thread de Windows : ramené sur celui de l'interface, où vit le coordinateur.
    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume) return;
        FileLog.Write("Sortie de veille");
        _ui.BeginInvoke(() => _coordinator.Host?.OnResumed());
    }

    // Une erreur imprévue dans un clic ou une relecture ne doit jamais fermer l'application résidente.
    private static void Safely(Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            FileLog.Write($"Erreur inattendue : {e}");
        }
    }

    private static void OpenLog()
    {
        if (!File.Exists(FileLog.Path)) FileLog.Write("Journal créé");
        Process.Start(new ProcessStartInfo(FileLog.Path) { UseShellExecute = true });
    }

    /// <summary>Crée les branchements Windows et l'hôte pour une configuration (port du coordinateur).</summary>
    private sealed class AdaptersFactory : IAcawServiceFactory
    {
        // Les objets COM de Windows doivent être créés hors du thread de l'interface (STA) :
        // l'hôte les utilisera depuis ses propres threads (MTA). Task.Run s'en charge.
        public IAcawService Create(AcawConfig config) =>
            Task.Run(() => Adapters.CreateAsync(config)).GetAwaiter().GetResult();
    }

    /// <summary>Les branchements Windows et l'hôte, créés et libérés ensemble.</summary>
    private sealed class Adapters(AcawHost host, SpeakerMute speakers, IDisposable[] owned) : IAcawService
    {
        public AcawHost Host { get; } = host;

        public static async Task<Adapters> CreateAsync(AcawConfig config)
        {
            var headset = new WindowsHeadset(config.Mac!);
            var media = await MediaSessions.CreateAsync();
            var radar = new AirPodsRadar(config.ModelNumber!.Value);
            var speakers = new SpeakerMute();
            var host = new AcawHost(config, headset, media, new AudioMeter(), speakers, radar,
                                    TimeProvider.System, FileLog.Write);
            return new Adapters(host, speakers, [host, radar, media, headset]);
        }

        public void Start() => Host.Start();

        public void Dispose()
        {
            foreach (var item in owned) item.Dispose(); // l'hôte d'abord : il attend la fin de son ordre en cours
            try
            {
                // Arrêté pendant l'arrivée des AirPods, l'hôte a pu laisser les haut-parleurs coupés (R7) :
                // on les rend. Sans effet si ACAW ne les avait pas coupés.
                speakers.Unmute();
            }
            catch (Exception e)
            {
                FileLog.Write($"Rétablissement du son impossible : {e.Message}");
            }
        }
    }
}
