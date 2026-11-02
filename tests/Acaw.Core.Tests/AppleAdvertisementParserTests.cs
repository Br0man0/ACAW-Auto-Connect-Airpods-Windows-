using Acaw.Core.Ble;

namespace Acaw.Core.Tests;

/// <summary>Trames reconstruites à partir des relevés des tests C1 et C3 de la phase 0.</summary>
public class AppleAdvertisementParserTests
{
    // Bloc 0x07 complet (25 octets) : 01 | modèle 27 20 | statut | batteries | charge+boîtier | couvercle | couleur | suffixe | 16 chiffrés
    private static byte[] Frame(byte status, byte batteries = 0x88, byte chargeAndCase = 0x8F, byte lid = 0x10) =>
        [0x07, 0x19, 0x01, 0x27, 0x20, status, batteries, chargeAndCase, lid, 0x00, 0x00, .. new byte[16]];

    [Fact]
    public void Lit_le_modele_dans_le_bon_ordre()
    {
        var frame = AppleAdvertisementParser.TryParse(Frame(0x0B));

        Assert.Equal((ushort)0x2027, frame?.Model);
    }

    [Theory]
    [InlineData(0x0B, 2, 0)] // C3 : les deux écouteurs portés
    [InlineData(0x03, 1, 0)] // C3 : un écouteur retiré
    [InlineData(0x33, 1, 1)] // C1 : un dans l'oreille, l'autre dans le boîtier
    [InlineData(0x31, 0, 1)] // C1 : un sorti et tenu en main
    [InlineData(0x75, 0, 2)] // C1 : les deux dans le boîtier ouvert
    public void Compte_les_ecouteurs_portes_et_ranges(byte status, int inEar, int inCase)
    {
        var frame = AppleAdvertisementParser.TryParse(Frame(status))!;

        Assert.Equal(inEar, frame.PodsInEar);
        Assert.Equal(inCase, frame.PodsInCase);
    }

    [Fact]
    public void Lit_les_batteries_et_signale_celle_du_boitier_inconnue()
    {
        var frame = AppleAdvertisementParser.TryParse(Frame(0x0B, batteries: 0x87, chargeAndCase: 0x8F))!;

        Assert.Equal(80, frame.BatteryA);
        Assert.Equal(70, frame.BatteryB);
        Assert.Null(frame.CaseBattery); // 15 = inconnu : aucun écouteur dans le boîtier
    }

    [Fact]
    public void Trouve_le_bloc_0x07_apres_d_autres_blocs()
    {
        byte[] data = [0x10, 0x02, 0xAA, 0xBB, .. Frame(0x0B)];

        Assert.NotNull(AppleAdvertisementParser.TryParse(data));
    }

    [Fact]
    public void Ignore_une_trame_sans_bloc_0x07()
    {
        Assert.Null(AppleAdvertisementParser.TryParse([0x10, 0x02, 0xAA, 0xBB]));
    }

    [Fact]
    public void Ignore_une_trame_tronquee()
    {
        Assert.Null(AppleAdvertisementParser.TryParse(Frame(0x0B)[..12]));
    }
}
