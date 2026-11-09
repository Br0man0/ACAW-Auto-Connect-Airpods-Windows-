using Acaw.Windows;

namespace Acaw.Windows.Tests;

/// <summary>
/// Le signal « arrête-toi proprement ». Chaque test a son propre nom d'événement : un vrai acaw-tray lancé
/// sur le PC n'est jamais touché. Pas de Trait « Windows » : tourne aussi sur GitHub.
/// </summary>
public class QuitSignalTests
{
    private readonly QuitSignal _signal = new($@"Local\ACAW.Tests.{Guid.NewGuid():N}");

    [Fact]
    public void La_demande_reveille_celui_qui_attend()
    {
        using var listening = _signal.Listen();

        _signal.Request(TimeSpan.Zero);

        Assert.True(listening.WaitOne(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Sans_rien_qui_attende_la_demande_ne_bloque_pas()
    {
        // ACAW n'est pas lancé : l'installateur ne doit pas rester coincé.
        _signal.Request(TimeSpan.Zero);
    }

    [Fact]
    public void Sans_demande_rien_ne_se_passe()
    {
        using var listening = _signal.Listen();

        Assert.False(listening.WaitOne(TimeSpan.FromMilliseconds(100)));
    }
}
