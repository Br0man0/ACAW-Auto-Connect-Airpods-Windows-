using System.Diagnostics;
using Acaw.Core.Config;
using Acaw.Core.Hosting;
using Acaw.Core.Switching;
using Acaw.Windows;
using Acaw.Windows.Audio;
using Acaw.Windows.Ble;
using Acaw.Windows.Bluetooth;
using Microsoft.Win32;

namespace Acaw.Tray;

/// <summary>
/// L'application de la barre des tâches : une icône, un menu, et l'hôte qui fait tout le travail.
/// Ce fichier ne contient aucune règle de bascule : il affiche l'état et transmet les clics.
/// </summary>
internal sealed class TrayApp : ApplicationContext
{
    private readonly NotifyIcon _icon = new() { Visible = true };
    private readonly ContextMenuStrip _menu = new();
    private readonly ToolStripMenuItem _status = new() { Enabled = false };
    private readonly ToolStripMenuItem _take = new("Amener sur ce PC");
    private readonly ToolStripMenuItem _release = new("Rendre à l'iPhone");
    private readonly ToolStripMenuItem _auto = new("Bascule automatique") { CheckOnClick = true };
    private readonly ToolStripMenuItem _startup = new("Démarrer avec Windows") { CheckOnClick = true };
    private readonly Control _ui = new(); // sert à revenir sur le thread de l'interface depuis l'hôte
    private readonly StartupShortcut _startupShortcut = StartupShortcut.ForCurrentUser(Application.ExecutablePath);
    private readonly Adapters? _adapters;

    public TrayApp()
    {
        _ui.CreateControl();
        BuildMenu();

        var config = LoadConfig();
        if (config is null) return; // message déjà affiché ; le menu permet d'ouvrir le journal ou de quitter

        // Les objets COM de Windows doivent être créés hors du thread de l'interface (STA) :
        // l'hôte les utilisera depuis ses propres threads (MTA). Task.Run s'en charge.
        FileLog.Write("Configuration lue, branchement aux services de Windows…");
        try
        {
            _adapters = Task.Run(() => Adapters.CreateAsync(config)).GetAwaiter().GetResult();
        }
        catch (Exception e)
        {
            // Contrôles média ou audio indisponibles : on le dit au lieu de planter au démarrage.
            Disable("Démarrage impossible", $"ACAW n'a pas pu démarrer : {e.Message}");
            FileLog.Write($"Démarrage impossible : {e}");
            return;
        }
        var host = _adapters.Host;
        host.StateChanged += state => _ui.BeginInvoke(() => Render(state));
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        host.Start();

        _auto.Checked = host.AutoSwitch;
        Render(host.State);
        FileLog.Write($"ACAW démarré pour {config.Name}");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged; // événement statique : à retirer sinon fuite
            _adapters?.Dispose();
            _icon.Visible = false;
            _icon.Dispose();
            _menu.Dispose();
            _ui.Dispose();
        }
        base.Dispose(disposing);
    }

    private void BuildMenu()
    {
        _take.Click += (_, _) => _adapters?.Host.Request(new TakeRequested());
        _release.Click += (_, _) => _adapters?.Host.Request(new ReleaseRequested());
        // Click et non CheckedChanged : cocher la case par le code (au démarrage) ne doit rien déclencher.
        _auto.Click += (_, _) => SetAutoSwitch(_auto.Checked);
        _startup.Click += (_, _) => SetStartup(_startup.Checked);

        _menu.Items.AddRange(
        [
            _status,
            new ToolStripSeparator(),
            _take,
            _release,
            new ToolStripSeparator(),
            _auto,
            _startup,
            new ToolStripMenuItem("Ouvrir le journal", null, (_, _) => OpenLog()),
            new ToolStripSeparator(),
            new ToolStripMenuItem("Quitter", null, (_, _) => ExitThread()),
        ]);
        _menu.Opening += (_, _) => _startup.Checked = _startupShortcut.IsEnabled;

        _icon.ContextMenuStrip = _menu;
        _icon.Icon = TrayIcons.For(SwitchState.Away, autoSwitch: false);
        _icon.Text = "ACAW";
        _status.Text = "Démarrage…";
    }

    private AcawConfig? LoadConfig()
    {
        string? problem;
        try
        {
            var config = ConfigStore.Load(ConfigStore.DefaultPath);
            if (config.IsConfigured && config.ModelNumber is not null) return config;
            problem = "Aucun AirPods configuré. Lance « acaw setup » dans un terminal, puis relance ACAW.";
        }
        catch (InvalidDataException e)
        {
            problem = $"{e.Message}. Relance « acaw setup », puis relance ACAW.";
        }

        FileLog.Write(problem);
        Disable("Non configuré", problem);
        return null;
    }

    // ACAW ne peut pas fonctionner : on garde l'icône (journal, Quitter) et on explique pourquoi.
    private void Disable(string status, string message)
    {
        _status.Text = status;
        _take.Enabled = _release.Enabled = _auto.Enabled = false;
        _icon.ShowBalloonTip(10_000, "ACAW", message, ToolTipIcon.Warning);
    }

    private void Render(SwitchState state)
    {
        bool auto = _adapters?.Host.AutoSwitch ?? false;
        string text = TrayIcons.Describe(state, auto);
        _status.Text = text;
        _icon.Text = text.Length <= 127 ? text : text[..127]; // limite de Windows pour l'info-bulle
        _icon.Icon = TrayIcons.For(state, auto);
        _take.Enabled = state is SwitchState.Away or SwitchState.Lost or SwitchState.Releasing;
        _release.Enabled = state is SwitchState.OnWindows or SwitchState.Switching;
    }

    private void SetAutoSwitch(bool on)
    {
        if (_adapters is null) return;
        _adapters.Host.SetAutoSwitch(on);
        try
        {
            // On relit le fichier avant d'écrire : « acaw setup » a pu le modifier entre-temps.
            var path = ConfigStore.DefaultPath;
            ConfigStore.Save(path, ConfigStore.Load(path) with { AutoSwitch = on });
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            FileLog.Write($"Réglage non enregistré : {e.Message}");
        }
        Render(_adapters.Host.State);
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
            _icon.ShowBalloonTip(5_000, "ACAW", $"Réglage impossible : {e.Message}", ToolTipIcon.Warning);
        }
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            FileLog.Write("Sortie de veille");
            _adapters?.Host.OnResumed();
        }
    }

    private static void OpenLog()
    {
        if (!File.Exists(FileLog.Path)) FileLog.Write("Journal créé");
        Process.Start(new ProcessStartInfo(FileLog.Path) { UseShellExecute = true });
    }

    /// <summary>Les branchements Windows et l'hôte, créés et libérés ensemble.</summary>
    private sealed class Adapters(AcawHost host, IDisposable[] owned) : IDisposable
    {
        public AcawHost Host { get; } = host;

        public static async Task<Adapters> CreateAsync(AcawConfig config)
        {
            var headset = new WindowsHeadset(config.Mac!);
            var media = await MediaSessions.CreateAsync();
            var radar = new AirPodsRadar(config.ModelNumber!.Value);
            var host = new AcawHost(config, headset, media, new AudioMeter(), new SpeakerMute(), radar,
                                    TimeProvider.System, FileLog.Write);
            return new Adapters(host, [host, radar, media, headset]);
        }

        public void Dispose()
        {
            foreach (var item in owned) item.Dispose();
        }
    }
}
