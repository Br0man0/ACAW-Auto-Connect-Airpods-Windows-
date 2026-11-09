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

    [Fact]
    [Trait("Categorie", "Windows")]
    public void La_liste_des_peripheriques_Bluetooth_se_lit_sans_erreur()
    {
        _ = HandsFreeProfile.IsEnabled(Mac); // vraie API CfgMgr : ne doit pas lever d'exception
    }
}
