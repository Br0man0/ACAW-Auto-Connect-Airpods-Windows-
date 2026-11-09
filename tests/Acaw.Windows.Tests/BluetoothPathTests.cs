using Acaw.Windows.Bluetooth;

namespace Acaw.Windows.Tests;

/// <summary>
/// Décodage du chemin du driver d'un endpoint Bluetooth. Les chemins viennent de vrais appareils (relevés
/// avec « phase0 list » le 2026-11-09), adresses MAC remplacées. Aucun matériel nécessaire : ces tests
/// tournent aussi sur GitHub.
/// </summary>
public class BluetoothPathTests
{
    private const string AirPods =
        @"{2}.\\?\bthenum#{0000110b-0000-1000-8000-00805f9b34fb}_vid&0001004c_pid&2027#7&ad2957b&0&a1b2c3d4e5f6_c00000000#{6994ad04-93ef-11d0-a3cc-00a0c9223196}\src";
    private const string JblSpeaker =
        @"{2}.\\?\bthenum#{0000110b-0000-1000-8000-00805f9b34fb}_vid&00010057_pid&0023#7&ad2957b&0&0a1b2c3d4e5f_c00000000#{6994ad04-93ef-11d0-a3cc-00a0c9223196}\src";
    private const string NoVendor =
        @"{2}.\\?\bthenum#{0000110b-0000-1000-8000-00805f9b34fb}_localmfg&0002#7&ad2957b&0&2064de441e29_c00000000#{6994ad04-93ef-11d0-a3cc-00a0c9223196}\src";
    private const string HandsFree =
        @"{2}.\\?\bthhfenum#bthhfpaudio#8&1798f2e4&0&97#{6994ad04-93ef-11d0-a3cc-00a0c9223196}\wave";
    private const string UsbHeadset =
        @"{2}.\\?\usb#vid_046d&pid_0a44&mi_00#7&2b5f1a3&0&0000#{6994ad04-93ef-11d0-a3cc-00a0c9223196}\global";

    [Fact]
    public void AirPods_donnent_MAC_modele_A2DP_et_Apple()
    {
        var ep = BluetoothAudio.FromDriverPath("id", "Casque (AirPods Pro)", AirPods);

        Assert.Equal("A1:B2:C3:D4:E5:F6", ep.Mac);
        Assert.Equal("2027", ep.ModelId);
        Assert.True(ep.IsA2dp);
        Assert.True(ep.IsApple);
    }

    [Fact]
    public void Enceinte_d_une_autre_marque_n_est_pas_Apple()
    {
        var ep = BluetoothAudio.FromDriverPath("id", "Haut-parleurs (JBL)", JblSpeaker);

        Assert.Equal("0A:1B:2C:3D:4E:5F", ep.Mac);
        Assert.True(ep.IsA2dp);
        Assert.False(ep.IsApple);
    }

    [Fact]
    public void Appareil_sans_fabricant_ni_modele()
    {
        var ep = BluetoothAudio.FromDriverPath("id", "Haut-parleurs (SRS-XB13)", NoVendor);

        Assert.Null(ep.ModelId);
        Assert.False(ep.IsApple);
        Assert.NotNull(ep.Mac);
    }

    [Fact]
    public void Mains_libres_sans_MAC_et_pas_A2DP()
    {
        var ep = BluetoothAudio.FromDriverPath("id", "Casque pour téléphone", HandsFree);

        Assert.Null(ep.Mac);
        Assert.False(ep.IsA2dp);
        Assert.False(ep.IsApple);
    }

    [Fact]
    public void Chemin_du_micro_donne_l_identifiant_de_son_peripherique()
    {
        Assert.Equal(@"bthhfenum\bthhfpaudio\8&1798f2e4&0&97", BluetoothAudio.InstanceIdFromPath(HandsFree));
        Assert.Null(BluetoothAudio.InstanceIdFromPath("pas un chemin"));
    }

    [Fact]
    public void Chemin_du_filtre_du_micro_a_partir_de_son_identifiant()
    {
        // Chemin relevé sur le micro des AirPods de Romsy (2026-11-09), ouvert sans droits administrateur.
        Assert.Equal(@"\\?\BTHHFENUM#BTHHFPAUDIO#8&E451131&D&97#{6994ad04-93ef-11d0-a3cc-00a0c9223196}\wave",
            BluetoothAudio.HandsFreeFilterPath(@"BTHHFENUM\BTHHFPAUDIO\8&E451131&D&97"));
    }

    [Fact]
    public void Identifiant_et_chemin_du_filtre_font_l_aller_retour()
    {
        string path = BluetoothAudio.HandsFreeFilterPath(@"BTHHFENUM\BTHHFPAUDIO\8&1798F2E4&0&97");
        Assert.Equal(@"BTHHFENUM\BTHHFPAUDIO\8&1798F2E4&0&97", BluetoothAudio.InstanceIdFromPath("{2}." + path));
    }

    [Fact]
    public void MAC_du_micro_lue_dans_l_identifiant_de_son_parent()
    {
        // Parent du micro des AirPods, relevé le 2026-11-09 (MAC remplacée).
        const string parent = @"BTHENUM\{0000111e-0000-1000-8000-00805f9b34fb}_VID&0001004c_PID&2027\7&ad2957b&0&A1B2C3D4E5F6_C00000000";

        Assert.Equal("A1:B2:C3:D4:E5:F6", BluetoothAudio.MacFromDeviceId(parent));
        Assert.Null(BluetoothAudio.MacFromDeviceId(@"BTH\MS_BTHBRB\6&1bc1a99c&0&1"));
    }

    [Fact]
    public void Le_micro_retenu_est_celui_des_memes_AirPods()
    {
        BtEndpoint[] endpoints =
        [
            new("musique", "Casque (AirPods)", "A1:B2:C3:D4:E5:F6", "2027", IsA2dp: true, IsApple: true),
            new("micro JBL", "Casque pour téléphone (JBL)", "0A:1B:2C:3D:4E:5F", null, IsA2dp: false, IsApple: false),
            new("micro AirPods", "Casque pour téléphone (AirPods)", "A1:B2:C3:D4:E5:F6", null, IsA2dp: false, IsApple: false),
        ];

        Assert.Equal("micro AirPods", BluetoothAudio.HandsFreeOf(endpoints, "a1:b2:c3:d4:e5:f6")?.Id);
        Assert.Null(BluetoothAudio.HandsFreeOf(endpoints, "11:22:33:44:55:66"));
    }

    [Fact]
    public void Seuls_les_chemins_Bluetooth_sont_retenus()
    {
        Assert.True(BluetoothAudio.IsBluetooth(AirPods));
        Assert.True(BluetoothAudio.IsBluetooth(HandsFree));
        Assert.False(BluetoothAudio.IsBluetooth(UsbHeadset));
    }
}
