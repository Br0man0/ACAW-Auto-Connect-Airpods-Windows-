using Acaw.Core.Config;
using Acaw.Core.Switching;

namespace Acaw.Core.Tests;

public class AirPodsPickerTests
{
    private static readonly PairedHeadset Mine = new("A1:B2:C3:D4:E5:F6", "2027", "AirPods Pro de Romsy", IsApple: true);
    private static readonly PairedHeadset Other = new("0A:1B:2C:3D:4E:5F", "2014", "AirPods de Lou", IsApple: true);
    private static readonly PairedHeadset Speaker = new("11:22:33:44:55:66", "0023", "Enceinte JBL", IsApple: false);
    private static readonly PairedHeadset NoModel = new("77:88:99:AA:BB:CC", null, "Apple sans modèle", IsApple: true);

    private static readonly AcawConfig Empty = new();
    private static readonly AcawConfig Configured = Empty.WithHeadset(Mine);

    [Fact]
    public void Une_seule_paire_d_AirPods_est_choisie_meme_avec_d_autres_casques()
    {
        var result = AirPodsPicker.Pick(Empty, [Speaker, Mine, NoModel]);

        Assert.Equal(PickReason.OnlyOne, result.Reason);
        Assert.Equal(Mine, result.Choice);
    }

    [Fact]
    public void Plusieurs_paires_ne_sont_jamais_choisies_au_hasard()
    {
        var result = AirPodsPicker.Pick(Empty, [Mine, Other]);

        Assert.Equal(PickReason.Several, result.Reason);
        Assert.Null(result.Choice);
    }

    [Fact]
    public void Sans_AirPods_rien_n_est_choisi()
    {
        var result = AirPodsPicker.Pick(Empty, [Speaker, NoModel]);

        Assert.Equal(PickReason.NoneFound, result.Reason);
        Assert.Null(result.Choice);
    }

    [Fact]
    public void Paire_configuree_toujours_appairee_reste()
    {
        var result = AirPodsPicker.Pick(Configured, [Other, Mine]);

        Assert.Equal(PickReason.Configured, result.Reason);
        Assert.Null(result.Choice);
    }

    [Fact]
    public void Adresse_comparee_sans_tenir_compte_des_majuscules()
    {
        var result = AirPodsPicker.Pick(Configured with { Mac = Mine.Mac.ToLowerInvariant() }, [Mine, Other]);

        Assert.Equal(PickReason.Configured, result.Reason);
    }

    [Fact]
    public void Paire_configuree_disparue_est_remplacee_par_la_seule_autre()
    {
        var result = AirPodsPicker.Pick(Configured, [Other, Speaker]);

        Assert.Equal(PickReason.Replaced, result.Reason);
        Assert.Equal(Other, result.Choice);
    }

    [Fact]
    public void Paire_configuree_disparue_sans_remplacante_evidente()
    {
        var other2 = Other with { Mac = "0A:0A:0A:0A:0A:0A" };

        Assert.Equal(PickReason.Orphan, AirPodsPicker.Pick(Configured, [Other, other2]).Reason);
        Assert.Equal(PickReason.Orphan, AirPodsPicker.Pick(Configured, [Speaker]).Reason);
    }

    [Fact]
    public void Liste_vide_au_demarrage_ne_rend_pas_la_configuration_orpheline()
    {
        // À l'ouverture de session, le service audio peut ne rien lister pendant quelques secondes.
        var result = AirPodsPicker.Pick(Configured, []);

        Assert.Equal(PickReason.Configured, result.Reason);
        Assert.Null(result.Choice);
    }

    [Fact]
    public void Modele_invalide_compte_comme_non_configure()
    {
        var broken = Configured with { ModelId = "zzzz" };

        Assert.False(AirPodsPicker.IsUsable(broken));
        Assert.Equal(PickReason.OnlyOne, AirPodsPicker.Pick(broken, [Mine]).Reason);
    }

    [Fact]
    public void Choisir_une_paire_garde_les_autres_reglages()
    {
        var custom = Empty with { AutoSwitch = false, Switching = new SwitchOptions { PauseDelay = TimeSpan.FromSeconds(9) } };

        var result = custom.WithHeadset(Mine);

        Assert.Equal(Mine.Mac, result.Mac);
        Assert.Equal("2027", result.ModelId);
        Assert.Equal(Mine.Name, result.Name);
        Assert.False(result.AutoSwitch);
        Assert.Equal(TimeSpan.FromSeconds(9), result.Switching.PauseDelay);
    }
}

public class ConfigChangeTests
{
    private static readonly AcawConfig Running = new() { Mac = "A1:B2:C3:D4:E5:F6", ModelId = "2027", Name = "AirPods" };

    [Fact]
    public void Premier_demarrage_cree_l_hote()
    {
        Assert.Equal(ConfigChange.Restart, ConfigChanges.Compare(null, Running));
    }

    [Fact]
    public void Configuration_identique_ne_fait_rien()
    {
        Assert.Equal(ConfigChange.Nothing, ConfigChanges.Compare(Running, Running with { }));
    }

    [Fact]
    public void Nom_seul_ne_fait_rien()
    {
        Assert.Equal(ConfigChange.Nothing, ConfigChanges.Compare(Running, Running with { Name = "Renommés" }));
    }

    [Fact]
    public void Bascule_automatique_seule_s_applique_sans_redemarrer()
    {
        Assert.Equal(ConfigChange.AutoSwitch, ConfigChanges.Compare(Running, Running with { AutoSwitch = false }));
    }

    [Fact]
    public void Autre_paire_autre_modele_ou_autre_delai_recree_l_hote()
    {
        Assert.Equal(ConfigChange.Restart, ConfigChanges.Compare(Running, Running with { Mac = "0A:1B:2C:3D:4E:5F" }));
        Assert.Equal(ConfigChange.Restart, ConfigChanges.Compare(Running, Running with { ModelId = "2014" }));
        var slower = Running with { Switching = new SwitchOptions { PauseDelay = TimeSpan.FromSeconds(9) } };
        Assert.Equal(ConfigChange.Restart, ConfigChanges.Compare(Running, slower));
    }

    [Fact]
    public void Delai_et_bascule_ensemble_recreent_l_hote()
    {
        var both = Running with { AutoSwitch = false, Switching = new SwitchOptions { Cooldown = TimeSpan.FromSeconds(1) } };

        Assert.Equal(ConfigChange.Restart, ConfigChanges.Compare(Running, both));
    }
}
