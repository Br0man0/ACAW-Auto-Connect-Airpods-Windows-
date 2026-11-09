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
    public void Seuls_les_chemins_Bluetooth_sont_retenus()
    {
        Assert.True(BluetoothAudio.IsBluetooth(AirPods));
        Assert.True(BluetoothAudio.IsBluetooth(HandsFree));
        Assert.False(BluetoothAudio.IsBluetooth(UsbHeadset));
    }
}
