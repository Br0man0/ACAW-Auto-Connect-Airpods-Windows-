namespace Acaw.Bluetooth.Takeover;

/// <summary>
/// La fenêtre qui suit un réveil du PC : un arrêt de session pendant cette fenêtre vient de la veille (USB
/// réénuméré, firmware à recharger) et ne compte pas comme un échec du garde-fou.
/// </summary>
public static class ResumeWindow
{
    /// <summary>Durée de la fenêtre (la même que la surveillance du firmware, <see cref="CommandWatch.ResumeWatch"/>).</summary>
    public static readonly TimeSpan Duration = CommandWatch.ResumeWatch;

    /// <summary>
    /// true si une session terminée à <paramref name="endedAt"/> s'est arrêtée à cause du réveil survenu à
    /// <paramref name="resumedAt"/> (null = pas de réveil récent). Un arrêt juste avant le réveil (mise en veille)
    /// compte aussi : la session meurt souvent pendant la veille et l'arrêt n'est vu qu'au réveil.
    /// </summary>
    public static bool Explains(DateTimeOffset? resumedAt, DateTimeOffset endedAt) =>
        resumedAt is { } resumed && endedAt >= resumed - TimeSpan.FromSeconds(5) && endedAt - resumed <= Duration;
}
