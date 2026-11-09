using Acaw.Core.Config;
using Acaw.Core.Switching;

namespace Acaw.Core.Hosting;

// Les ports du coordinateur : ce dont il a besoin, décrit sans rien savoir de Windows ni des fichiers.

/// <summary>Où la configuration est rangée (config.json, ou un faux dans les tests).</summary>
public interface IConfigSource
{
    /// <summary>Lit la configuration ; une configuration vide si le fichier n'existe pas.</summary>
    /// <exception cref="InvalidDataException">Contenu invalide (JSON abîmé, délai négatif…).</exception>
    /// <exception cref="IOException">Fichier momentanément inaccessible (en cours d'écriture par un éditeur).</exception>
    AcawConfig Load();

    void Save(AcawConfig config);
}

/// <summary>Les casques Bluetooth appairés au PC.</summary>
public interface IHeadsetCatalog
{
    /// <summary>Liste les casques ; une liste vide si Windows ne peut pas répondre.</summary>
    IReadOnlyList<PairedHeadset> List();
}

/// <summary>Un hôte et les branchements Windows qu'il utilise, créés et arrêtés ensemble.</summary>
public interface IAcawService : IDisposable
{
    AcawHost Host { get; }

    /// <summary>Démarre l'hôte (séparé de la création : on crée d'abord, on démarre une fois l'ancien arrêté).</summary>
    void Start();

    // Dispose arrête l'hôte (qui attend la fin de son ordre en cours), PUIS rétablit le son qu'il a pu laisser
    // coupé (règle R7) : dans cet ordre, l'hôte ne peut plus le recouper.
}

public interface IAcawServiceFactory
{
    /// <summary>Crée sans démarrer. Peut échouer (service audio indisponible…) : le coordinateur garde alors l'ancien.</summary>
    IAcawService Create(AcawConfig config);
}

/// <summary>
/// Choisit la configuration en service et l'applique (ADR-0002, mise à jour « configuration à chaud ») :
/// sélection automatique des AirPods, relecture de config.json, choix dans le menu. Une nouvelle
/// configuration recrée l'hôte, dans cet ordre : créer le nouveau → arrêter l'ancien → démarrer le nouveau.
/// Si la création échoue, l'ancien continue. Pas thread-safe : tout passe par un seul thread (celui de l'icône).
/// </summary>
public sealed class ConfigCoordinator(IConfigSource source, IHeadsetCatalog catalog, IAcawServiceFactory factory,
                                      Action<string> log) : IDisposable
{
    private IAcawService? _service;
    private string? _lastProblem; // dernier message montré : on ne répète pas la même bulle
    private bool _reported;       // un message a été émis pendant ce Refresh

    /// <summary>La configuration de l'hôte en marche, ou null s'il n'y en a pas.</summary>
    public AcawConfig? Config { get; private set; }

    /// <summary>L'hôte en marche, ou null (non configuré, ou création impossible).</summary>
    public AcawHost? Host => _service?.Host;

    /// <summary>Pourquoi il n'y a pas d'hôte, en une phrase pour l'utilisateur ; null s'il y en a un.</summary>
    public string? Problem { get; private set; }

    /// <summary>L'hôte a été créé, remplacé, ou l'état « pas d'hôte » a changé. Levé sur le thread de l'appelant.</summary>
    public event Action? Changed;

    /// <summary>Un message à montrer à l'utilisateur (bulle). Jamais deux fois de suite le même.</summary>
    public event Action<string>? Notice;

    /// <summary>
    /// Relit la configuration et l'applique : au démarrage, quand config.json change, et régulièrement tant
    /// qu'il n'y a pas d'hôte. Renvoie false si le fichier était momentanément inaccessible (à réessayer bientôt).
    /// </summary>
    public bool Refresh()
    {
        _reported = false;
        bool done = TryRefresh();
        if (done && !_reported) _lastProblem = null; // tout va bien : un problème qui reviendra sera signalé à nouveau
        return done;
    }

    private bool TryRefresh()
    {
        AcawConfig next;
        try
        {
            next = source.Load();
        }
        catch (IOException e)
        {
            log($"Configuration momentanément inaccessible ({e.Message}), nouvel essai");
            return false;
        }
        catch (InvalidDataException e)
        {
            // Un fichier abîmé ne fait rien perdre : on garde ce qui tourne.
            Report(Host is null ? e.Message : $"{e.Message}. La configuration précédente est gardée.");
            if (Host is null) SetProblem(e.Message);
            return true;
        }

        var paired = catalog.List();
        var pick = AirPodsPicker.Pick(next, paired);
        if (pick.Choice is { } chosen)
        {
            next = next.WithHeadset(chosen);
            TrySave(next);
            Report(pick.Reason == PickReason.Replaced
                ? $"Les AirPods configurés ne sont plus appairés : ACAW pilote maintenant {chosen.Name}."
                : $"ACAW pilote {chosen.Name}. Pour en changer : menu de l'icône → AirPods.");
        }
        else if (pick.Reason == PickReason.Orphan)
        {
            Report($"{next.Name} n'est plus appairé à ce PC. Choisis tes AirPods dans le menu de l'icône.");
        }

        if (!AirPodsPicker.IsUsable(next))
        {
            if (paired.Count == 0)
            {
                // À l'ouverture de session, Windows peut ne rien lister pendant quelques secondes : pas de bulle,
                // le prochain essai (toutes les 30 s) dira la vérité.
                if (Host is null) SetProblem("Recherche des AirPods…");
                return true;
            }
            string why = pick.Reason == PickReason.Several
                ? "Plusieurs AirPods sont appairés : choisis les tiens dans le menu de l'icône → AirPods."
                : "Aucun AirPods appairé à ce PC. Appaire-les dans les paramètres Bluetooth de Windows.";
            Report(Host is null ? why : $"{why} La configuration précédente est gardée.");
            if (Host is null) SetProblem(why);
            return true;
        }

        Apply(next);
        return true;
    }

    /// <summary>Les casques qu'ACAW sait piloter, pour le menu.</summary>
    public IReadOnlyList<PairedHeadset> Candidates() => catalog.List().Where(AirPodsPicker.IsCandidate).ToList();

    /// <summary>L'utilisateur choisit une paire dans le menu : enregistrée, puis appliquée tout de suite.</summary>
    public void Choose(PairedHeadset headset)
    {
        var next = ReadForUpdate().WithHeadset(headset);
        TrySave(next);
        _lastProblem = null; // un choix explicite : les anciens messages ne comptent plus
        Apply(next);
    }

    /// <summary>Le menu suspend ou reprend la bascule automatique : appliqué à l'hôte et enregistré.</summary>
    public void SetAutoSwitch(bool on)
    {
        if (_service is null || Config is null) return;
        _service.Host.SetAutoSwitch(on);
        Config = Config with { AutoSwitch = on }; // avant l'écriture : la relecture qui suivra ne verra aucun changement
        TrySave(ReadForUpdate() with { AutoSwitch = on });
    }

    public void Dispose()
    {
        _service?.Dispose();
        _service = null;
    }

    private void Apply(AcawConfig next)
    {
        switch (ConfigChanges.Compare(Host is null ? null : Config, next))
        {
            case ConfigChange.Nothing:
                Config = next;
                return;
            case ConfigChange.AutoSwitch:
                _service!.Host.SetAutoSwitch(next.AutoSwitch);
                Config = next;
                Changed?.Invoke();
                return;
        }

        IAcawService created;
        try
        {
            created = factory.Create(next);
        }
        catch (Exception e)
        {
            string why = $"ACAW n'a pas pu démarrer : {e.Message}";
            string message = Host is null ? why : $"{why}. La configuration précédente est gardée.";
            // Le détail complet une seule fois : les nouveaux essais (toutes les 30 s) n'inondent pas le journal.
            if (message != _lastProblem) log($"Démarrage impossible avec la nouvelle configuration : {e}");
            Report(message);
            if (Host is null) SetProblem(why);
            return;
        }

        _service?.Dispose(); // l'ancien s'arrête (et rend le son) AVANT que le nouveau démarre
        _service = created;
        Config = next;
        Problem = null;
        _lastProblem = null;
        created.Start();
        log($"Configuration appliquée : {next.Name}");
        Changed?.Invoke();
    }

    // Relit le fichier avant de le modifier (« acaw setup » a pu l'écrire entre-temps) ; s'il est illisible,
    // on repart de la configuration en service.
    private AcawConfig ReadForUpdate()
    {
        try
        {
            return source.Load();
        }
        catch (Exception e) when (e is IOException or InvalidDataException)
        {
            return Config ?? new AcawConfig();
        }
    }

    private void TrySave(AcawConfig config)
    {
        try
        {
            source.Save(config);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // L'application continue avec la configuration en mémoire ; elle sera redemandée au prochain lancement.
            log($"Configuration non enregistrée : {e.Message}");
        }
    }

    private void SetProblem(string why)
    {
        if (Problem == why) return;
        Problem = why;
        Changed?.Invoke();
    }

    // Montré (bulle) et noté (journal) une seule fois tant que le message ne change pas.
    private void Report(string message)
    {
        _reported = true;
        if (message == _lastProblem) return;
        _lastProblem = message;
        log(message);
        Notice?.Invoke(message);
    }
}
