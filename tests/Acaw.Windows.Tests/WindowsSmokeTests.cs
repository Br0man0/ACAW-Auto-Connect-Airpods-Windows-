using System.Runtime.InteropServices;
using Acaw.Core.Config;
using Acaw.Core.Hosting;
using Acaw.Windows.Audio;
using Acaw.Windows.Ble;
using Acaw.Windows.Bluetooth;

namespace Acaw.Windows.Tests;

/// <summary>
/// Tests « fumée » : ils appellent les vraies API de Windows pour vérifier que les branchements tiennent
/// (après une mise à jour de Windows, de .NET ou de CsWin32, par exemple). Ils ne MODIFIENT rien : aucune
/// connexion, aucune déconnexion, aucune mise en pause. Ils tournent sur n'importe quel PC Windows, avec ou
/// sans AirPods ; les vérifications qui demandent des AirPods configurés sont sautées sinon.
/// À exclure d'une machine sans Windows : dotnet test --filter "Categorie!=Windows".
/// </summary>
[Trait("Categorie", "Windows")]
public class WindowsSmokeTests
{
    // Erreur renvoyée par Windows quand le Bluetooth est coupé : acceptable dans un test fumée.
    private const int DeviceNotAvailable = unchecked((int)0x800710DF);

    [Fact]
    public void La_liste_des_casques_Bluetooth_se_lit_sans_erreur()
    {
        var endpoints = BluetoothAudio.ListEndpoints();

        Assert.All(endpoints, ep =>
        {
            Assert.False(string.IsNullOrEmpty(ep.Id));
            if (ep.Mac is not null) Assert.Matches("^([0-9A-F]{2}:){5}[0-9A-F]{2}$", ep.Mac);
        });
    }

    [Fact]
    public void Une_MAC_inconnue_ne_trouve_aucun_casque()
    {
        Assert.Null(BluetoothAudio.FindA2dp("00:00:00:00:00:01"));
    }

    [Fact]
    public void Le_volume_se_lit_et_reste_entre_0_et_1()
    {
        var meter = new AudioMeter();

        for (int i = 0; i < 3; i++) // le 2e relevé utilise la sortie gardée en mémoire
            Assert.InRange(meter.ReadPeak(), 0f, 1f);
    }

    [Fact]
    public async Task Les_controles_media_de_Windows_sont_accessibles()
    {
        using var media = await MediaSessions.CreateAsync();

        _ = media.IsPlaying; // ne doit pas lever d'exception
    }

    [Fact]
    public void L_ecoute_BLE_demarre_et_s_arrete()
    {
        try
        {
            using var scanner = new BleScanner(0x2027);
            scanner.Start();
        }
        catch (COMException e) when (e.HResult == DeviceNotAvailable)
        {
            // Bluetooth coupé sur ce PC : le branchement est bon, Windows refuse juste d'écouter.
        }
    }

    [Fact]
    public void Les_AirPods_configures_ont_un_etat_lisible()
    {
        var config = ConfigStore.Load(ConfigStore.DefaultPath);
        if (!config.IsConfigured) return; // pas d'AirPods configurés sur ce PC : rien à vérifier

        using var headset = new WindowsHeadset(config.Mac!);

        Assert.True(Enum.IsDefined(headset.GetState()));
        Assert.NotNull(config.ModelNumber);
    }

    // On ne teste PAS Mute ici : ce serait couper vraiment le son du PC. Seulement les cas sans effet.
    [Fact]
    public void Retablir_sans_coupure_prealable_ne_touche_a_rien()
    {
        string marker = Path.Combine(Path.GetTempPath(), $"acaw-mute-{Guid.NewGuid():N}.txt");

        new SpeakerMute(marker).Unmute();

        Assert.False(File.Exists(marker));
    }

    [Fact]
    public void Retablir_une_sortie_disparue_nettoie_sans_erreur()
    {
        string marker = Path.Combine(Path.GetTempPath(), $"acaw-mute-{Guid.NewGuid():N}.txt");
        File.WriteAllText(marker, "{0.0.0.00000000}.{00000000-0000-0000-0000-000000000000}");

        new SpeakerMute(marker).Unmute();

        Assert.False(File.Exists(marker));
    }

    [Fact]
    public void Un_casque_introuvable_est_signale_absent_sans_exception()
    {
        using var headset = new WindowsHeadset("00:00:00:00:00:01");

        Assert.Equal(HeadsetState.Absent, headset.GetState());
        Assert.Throws<InvalidOperationException>(headset.Connect);
    }
}
