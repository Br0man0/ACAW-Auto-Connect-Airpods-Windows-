using System.Numerics;

namespace Acaw.Core.Ble;

/// <summary>
/// Partie en clair d'une trame « proximity pairing » (type 0x07) des AirPods.
/// Signification des champs établie pendant la phase 0 : voir docs/protocole-ble-airpods.md.
/// </summary>
/// <param name="Model">Modèle, égal au « pid » du driver Bluetooth classique (ex. 0x2027).</param>
public sealed record ProximityFrame(ushort Model, byte Status, byte Batteries, byte ChargeAndCase, byte Lid, byte Color)
{
    /// <summary>Nombre d'écouteurs portés (bits 0x02 et 0x08 du statut). Le côté n'est pas connu.</summary>
    public int PodsInEar => BitOperations.PopCount((uint)(Status & 0x0A));

    /// <summary>Nombre d'écouteurs dans le boîtier : bit 0x04 = les deux, bit 0x10 = au moins un.</summary>
    public int PodsInCase => (Status & 0x04) != 0 ? 2 : (Status & 0x10) != 0 ? 1 : 0;

    /// <summary>Batterie d'un écouteur en %, ou null si inconnue. Le côté n'est pas connu.</summary>
    public int? BatteryA => Percent(Batteries >> 4);

    /// <inheritdoc cref="BatteryA"/>
    public int? BatteryB => Percent(Batteries & 0x0F);

    /// <summary>Batterie du boîtier en %, connue seulement si un écouteur y est posé.</summary>
    public int? CaseBattery => Percent(ChargeAndCase & 0x0F);

    // Niveau sur 0..10 (×10 %) ; 15 signifie « inconnu ».
    private static int? Percent(int nibble) => nibble <= 10 ? nibble * 10 : null;
}
