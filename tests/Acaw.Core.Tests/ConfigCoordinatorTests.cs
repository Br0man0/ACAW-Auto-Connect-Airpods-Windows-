using Acaw.Core.Config;
using Acaw.Core.Hosting;
using Acaw.Core.Switching;

namespace Acaw.Core.Tests;

/// <summary>
/// Le coordinateur de configuration, avec de faux fichier, catalogue et fabrique. Chaque test raconte une
/// situation : premier lancement, config.json modifié, AirPods changés, service Windows indisponible…
/// </summary>
public sealed class ConfigCoordinatorTests : IDisposable
{
    private static readonly PairedHeadset Mine = new("A1:B2:C3:D4:E5:F6", "2027", "AirPods de Romsy", IsApple: true);
    private static readonly PairedHeadset Lou = new("0A:1B:2C:3D:4E:5F", "2014", "AirPods de Lou", IsApple: true);
    private static readonly PairedHeadset Speaker = new("11:22:33:44:55:66", "0023", "Enceinte JBL", IsApple: false);

    private readonly List<string> _journal = [];
    private readonly List<string> _notices = [];
    private readonly FakeConfigSource _file = new();
    private readonly FakeCatalog _catalog = new();
    private readonly FakeServiceFactory _factory;
    private readonly ConfigCoordinator _coordinator;
    private int _changes;

    public ConfigCoordinatorTests()
    {
        _factory = new FakeServiceFactory(_journal);
        _coordinator = new ConfigCoordinator(_file, _catalog, _factory, _ => { });
        _coordinator.Notice += _notices.Add;
        _coordinator.Changed += () => _changes++;
    }

    public void Dispose() => _coordinator.Dispose();

    private void RunningWith(PairedHeadset headset)
    {
        _catalog.Paired = [headset];
        _file.Stored = new AcawConfig().WithHeadset(headset);
        _coordinator.Refresh();
        _journal.Clear();
        _notices.Clear();
        _changes = 0;
    }

    [Fact]
    public void Premier_lancement_avec_une_seule_paire_la_choisit_l_enregistre_et_demarre()
    {
        _catalog.Paired = [Speaker, Mine];

        Assert.True(_coordinator.Refresh());

        Assert.Equal(Mine.Mac, _file.Stored.Mac);
        Assert.Equal(["créé #1 AirPods de Romsy", "démarré #1 AirPods de Romsy"], _journal);
        Assert.NotNull(_coordinator.Host);
        Assert.Null(_coordinator.Problem);
        Assert.Single(_notices); // « ACAW pilote … »
    }

    [Fact]
    public void Plusieurs_paires_pas_d_hote_et_un_seul_message_meme_en_reessayant()
    {
        _catalog.Paired = [Mine, Lou];

        _coordinator.Refresh();
        _coordinator.Refresh(); // l'essai toutes les 30 s

        Assert.Null(_coordinator.Host);
        Assert.Contains("Plusieurs AirPods", _coordinator.Problem);
        Assert.Single(_notices);
        Assert.Equal(0, _file.Saves); // jamais de choix au hasard
    }

    [Fact]
    public void Les_essais_toutes_les_30_s_n_inondent_pas_le_journal()
    {
        var log = new List<string>();
        using var coordinator = new ConfigCoordinator(_file, _catalog, _factory, log.Add);
        _catalog.Paired = [Mine];
        _factory.Fails = true;

        for (int i = 0; i < 10; i++) coordinator.Refresh(); // cinq minutes de nouveaux essais

        Assert.Single(log, line => line.StartsWith("Démarrage impossible", StringComparison.Ordinal));
        Assert.Single(log, line => line.StartsWith("ACAW n'a pas pu démarrer", StringComparison.Ordinal));
    }

    [Fact]
    public void Liste_vide_a_l_ouverture_de_session_attend_sans_bulle_puis_demarre()
    {
        _coordinator.Refresh();

        Assert.Equal("Recherche des AirPods…", _coordinator.Problem);
        Assert.Empty(_notices);

        _catalog.Paired = [Mine]; // le service audio répond enfin
        _coordinator.Refresh();

        Assert.NotNull(_coordinator.Host);
        Assert.Null(_coordinator.Problem);
    }

    [Fact]
    public void Delai_modifie_recree_l_hote_en_creant_le_nouveau_avant_d_arreter_l_ancien()
    {
        RunningWith(Mine);
        var old = _factory.Created[0];
        old.Speakers.Mute(); // les AirPods étaient en train d'arriver : haut-parleurs coupés
        _file.Stored = _file.Stored with { Switching = new SwitchOptions { PauseDelay = TimeSpan.FromSeconds(9) } };

        _coordinator.Refresh();

        Assert.Equal(["créé #2 AirPods de Romsy", "arrêté #1 AirPods de Romsy", "démarré #2 AirPods de Romsy"], _journal);
        Assert.True(old.Disposed);
        Assert.False(old.Speakers.Muted); // le son est rendu à l'arrêt
        Assert.Equal(TimeSpan.FromSeconds(9), _coordinator.Config!.Switching.PauseDelay);
        Assert.Equal(1, _changes);
    }

    [Fact]
    public void Creation_qui_echoue_garde_l_ancien_hote_en_marche()
    {
        RunningWith(Mine);
        var old = _factory.Created[0];
        _factory.Fails = true;
        _file.Stored = _file.Stored with { Switching = new SwitchOptions { Cooldown = TimeSpan.FromSeconds(1) } };

        _coordinator.Refresh();

        Assert.False(old.Disposed);
        Assert.Same(old.Host, _coordinator.Host);
        Assert.Contains("précédente est gardée", Assert.Single(_notices));
    }

    [Fact]
    public void Creation_qui_echoue_au_demarrage_puis_reussit_au_nouvel_essai()
    {
        _catalog.Paired = [Mine];
        _factory.Fails = true;

        _coordinator.Refresh();
        Assert.Null(_coordinator.Host);
        Assert.Contains("n'a pas pu démarrer", _coordinator.Problem);

        _factory.Fails = false;
        _coordinator.Refresh();
        Assert.NotNull(_coordinator.Host);
        Assert.Null(_coordinator.Problem);
    }

    [Fact]
    public void Fichier_abime_garde_la_configuration_et_ne_previent_qu_une_fois()
    {
        RunningWith(Mine);
        _file.LoadFails = new InvalidDataException("Configuration illisible (config.json)");

        _coordinator.Refresh();
        _coordinator.Refresh();

        Assert.Single(_notices);
        Assert.Empty(_journal); // ni arrêt ni création
        Assert.NotNull(_coordinator.Host);
    }

    [Fact]
    public void Probleme_resolu_puis_revenu_est_signale_a_nouveau()
    {
        RunningWith(Mine);
        _file.LoadFails = new InvalidDataException("Configuration illisible");
        _coordinator.Refresh();
        _file.LoadFails = null;
        _coordinator.Refresh(); // corrigé
        _file.LoadFails = new InvalidDataException("Configuration illisible");
        _coordinator.Refresh(); // abîmé de nouveau

        Assert.Equal(2, _notices.Count);
    }

    [Fact]
    public void Fichier_verrouille_par_un_editeur_demande_un_nouvel_essai_sans_message()
    {
        RunningWith(Mine);
        _file.LoadFails = new IOException("fichier utilisé par un autre processus");

        Assert.False(_coordinator.Refresh());

        Assert.Empty(_notices);
        Assert.Empty(_journal);
    }

    [Fact]
    public void Bascule_automatique_changee_dans_le_fichier_sans_recreer_l_hote()
    {
        RunningWith(Mine);
        _file.Stored = _file.Stored with { AutoSwitch = false };

        _coordinator.Refresh();
        _coordinator.Host!.ProcessPending();

        Assert.Empty(_journal);
        Assert.False(_coordinator.Host.AutoSwitch);
        Assert.Equal(1, _changes);
    }

    [Fact]
    public void Bascule_suspendue_par_le_menu_est_enregistree_et_la_relecture_ne_recree_rien()
    {
        RunningWith(Mine);

        _coordinator.SetAutoSwitch(false);
        _coordinator.Refresh(); // la surveillance du fichier voit l'écriture

        Assert.False(_file.Stored.AutoSwitch);
        Assert.Empty(_journal);
    }

    [Fact]
    public void Choix_dans_le_menu_enregistre_garde_les_delais_et_change_d_hote()
    {
        _catalog.Paired = [Mine, Lou];
        _file.Stored = new AcawConfig { Switching = new SwitchOptions { PauseDelay = TimeSpan.FromSeconds(9) } }.WithHeadset(Mine);
        _coordinator.Refresh();

        _coordinator.Choose(Lou);

        Assert.Equal(Lou.Mac, _file.Stored.Mac);
        Assert.Equal(TimeSpan.FromSeconds(9), _file.Stored.Switching.PauseDelay);
        Assert.Equal(Lou.Mac, _coordinator.Config!.Mac);
        Assert.True(_factory.Created[0].Disposed);
        Assert.True(_factory.Created[1].Started);
    }

    [Fact]
    public void Choix_dans_le_menu_sort_de_l_etat_non_configure()
    {
        _catalog.Paired = [Mine, Lou];
        _coordinator.Refresh();
        Assert.Null(_coordinator.Host);

        _coordinator.Choose(Mine);

        Assert.NotNull(_coordinator.Host);
        Assert.Null(_coordinator.Problem);
    }

    [Fact]
    public void AirPods_remplaces_la_nouvelle_paire_est_prise()
    {
        RunningWith(Mine);
        _catalog.Paired = [Lou, Speaker]; // les anciens ont été supprimés de Windows

        _coordinator.Refresh();

        Assert.Equal(Lou.Mac, _coordinator.Config!.Mac);
        Assert.Contains("ne sont plus appairés", Assert.Single(_notices));
    }

    [Fact]
    public void Nom_seul_change_ne_recree_rien()
    {
        // Les AirPods ont été renommés dans les paramètres Bluetooth de Windows : c'est lui qui fait foi.
        RunningWith(Mine);
        _catalog.Paired = [Mine with { Name = "Renommés" }];

        _coordinator.Refresh();

        Assert.Empty(_journal);
        Assert.Equal("Renommés", _coordinator.Config!.Name);
        Assert.Equal("Renommés", _file.Stored.Name);
    }

    [Fact]
    public void Nom_illisible_est_corrige_des_que_Windows_le_donne()
    {
        // Vu dans le journal de Romsy le 2026-11-09 : « ACAW démarré pour (nom illisible) ».
        RunningWith(Mine with { Name = PairedHeadset.UnreadableName });
        _catalog.Paired = [Mine]; // plus tard, Windows sait lire le nom

        _coordinator.Refresh();

        Assert.Equal(Mine.Name, _file.Stored.Name);
        Assert.Equal(Mine.Name, _coordinator.Config!.Name);
        Assert.Empty(_journal); // le nom seul ne recrée pas l'hôte
    }

    [Fact]
    public void Nom_illisible_ne_remplace_jamais_un_bon_nom()
    {
        RunningWith(Mine);
        _catalog.Paired = [Mine with { Name = PairedHeadset.UnreadableName }];

        _coordinator.Refresh();

        Assert.Equal(Mine.Name, _file.Stored.Name);
    }

    [Fact]
    public void Choix_du_micro_est_retenu_sans_recreer_l_hote()
    {
        RunningWith(Mine);

        _coordinator.SetMicrophone(true);
        _coordinator.Refresh(); // la surveillance du fichier voit l'écriture

        Assert.True(_file.Stored.Microphone);
        Assert.True(_coordinator.Config!.Microphone);
        Assert.Empty(_journal);
    }

    [Fact]
    public void Candidats_du_menu_sont_les_seuls_appareils_Apple_avec_un_modele()
    {
        _catalog.Paired = [Speaker, Mine, Lou with { ModelId = null }];

        Assert.Equal([Mine], _coordinator.Candidates());
    }

    [Fact]
    public void Arret_du_coordinateur_arrete_l_hote()
    {
        RunningWith(Mine);

        _coordinator.Dispose();

        Assert.True(_factory.Created[0].Disposed);
    }
}
