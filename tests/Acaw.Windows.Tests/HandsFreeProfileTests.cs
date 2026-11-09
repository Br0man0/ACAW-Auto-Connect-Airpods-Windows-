using Acaw.Windows.Bluetooth;

namespace Acaw.Windows.Tests;

/// <summary>
/// Détection du profil mains libres. Les identifiants viennent de vrais périphériques (relevés le 2026-11-09
/// sur le PC de Romsy, profil retiré), adresse remplacée ; la ligne « Handsfree » est construite sur le même
/// modèle. Sans matériel : ces tests tournent aussi sur GitHub.
/// </summary>
public class HandsFreeProfileTests
{
    private const string Mac = "A1:B2:C3:D4:E5:F6";

    private static readonly string[] WithoutHandsFree =
    [
        @"BTHENUM\{0000110B-0000-1000-8000-00805F9B34FB}_VID&0001004C_PID&2027\7&AD2957B&0&A1B2C3D4E5F6_C00000000",
        @"BTHENUM\{0000110E-0000-1000-8000-00805F9B34FB}_VID&0001004C_PID&2027\7&AD2957B&0&A1B2C3D4E5F6_C00000000",
        @"BTHENUM\{74EC2172-0BAD-4D01-8F77-997B2BE0722A}_VID&0001004C_PID&2027\7&AD2957B&0&A1B2C3D4E5F6_C00000000",
        @"BTHENUM\DEV_A1B2C3D4E5F6\7&AD2957B&0&BLUETOOTHDEVICE_A1B2C3D4E5F6",
    ];

    private const string HandsFree =
        @"BTHENUM\{0000111E-0000-1000-8000-00805F9B34FB}_VID&0001004C_PID&2027\7&AD2957B&0&A1B2C3D4E5F6_C00000000";

    [Fact]
    public void Profil_retire_n_est_pas_signale()
    {
        Assert.False(HandsFreeProfile.IsEnabledIn(WithoutHandsFree, Mac));
    }

    [Fact]
    public void Profil_actif_est_signale()
    {
        Assert.True(HandsFreeProfile.IsEnabledIn([.. WithoutHandsFree, HandsFree], Mac));
    }

    [Fact]
    public void Profil_actif_d_un_autre_casque_ne_compte_pas()
    {
        string otherHeadset = HandsFree.Replace("A1B2C3D4E5F6", "0A1B2C3D4E5F", StringComparison.Ordinal);

        Assert.False(HandsFreeProfile.IsEnabledIn([.. WithoutHandsFree, otherHeadset], Mac));
    }

    [Fact]
    public void Adresse_comparee_sans_tenir_compte_des_majuscules()
    {
        Assert.True(HandsFreeProfile.IsEnabledIn([HandsFree.ToLowerInvariant()], Mac));
    }

    [Theory]
    [InlineData("F4:78:AC:EA:15:66", 0xF478ACEA1566UL)]
    [InlineData("a1-b2-c3-d4-e5-f6", 0xA1B2C3D4E5F6UL)]
    [InlineData("00:00:00:00:00:01", 1UL)]
    public void Adresse_convertie_au_format_des_API_Bluetooth(string mac, ulong expected)
    {
        Assert.Equal(expected, HandsFreeProfile.ParseAddress(mac));
    }

    [Theory]
    [InlineData(0u, true, HandsFreeResult.Done)]
    [InlineData(0x80070057u, true, HandsFreeResult.AlreadyInState)]
    [InlineData(1168u, false, HandsFreeResult.AlreadyInState)] // mesuré : retirer un profil déjà absent
    [InlineData(1168u, true, HandsFreeResult.Failed)]          // en activation, « introuvable » est une vraie erreur
    [InlineData(5u, false, HandsFreeResult.AccessDenied)]
    [InlineData(1060u, true, HandsFreeResult.NotSupported)]
    [InlineData(31u, false, HandsFreeResult.Failed)]
    public void Codes_de_retour_de_Windows_traduits(uint code, bool enabling, HandsFreeResult expected)
    {
        Assert.Equal(expected, HandsFreeProfile.ToResult(code, enabling));
    }

    [Fact]
    public void Deja_dans_l_etat_voulu_est_un_succes_pour_l_utilisateur()
    {
        // Cocher la case alors que le profil est déjà actif ne doit pas afficher d'erreur.
        Assert.Equal(HandsFreeProfile.Describe(HandsFreeResult.Done, enabled: true),
                     HandsFreeProfile.Describe(HandsFreeResult.AlreadyInState, enabled: true));
    }

    [Fact]
    public void Acces_refuse_explique_quoi_faire()
    {
        Assert.Contains("Propriétés", HandsFreeProfile.Describe(HandsFreeResult.AccessDenied, enabled: false));
    }

    [Fact]
    [Trait("Categorie", "Windows")]
    public void La_liste_des_peripheriques_Bluetooth_se_lit_sans_erreur()
    {
        _ = HandsFreeProfile.IsEnabled(Mac); // vraie API CfgMgr : ne doit pas lever d'exception
    }
}
