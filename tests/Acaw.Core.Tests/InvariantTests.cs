using Acaw.Core.Audio;
using Acaw.Core.Switching;
using Microsoft.Extensions.Time.Testing;
using static Acaw.Core.Switching.SwitchCommand;

namespace Acaw.Core.Tests;

/// <summary>
/// Tests d'invariants : au lieu d'écrire chaque scénario à la main, on génère des milliers de séquences
/// d'événements au hasard (y compris absurdes) et on vérifie des règles qui doivent TOUJOURS tenir.
/// Le hasard est « graine fixe » : chaque séquence est reproductible, et un échec affiche sa graine.
/// </summary>
public class InvariantTests
{
    private const int Sequences = 2000;
    private const int StepsPerSequence = 200;

    private static readonly SwitchEvent[] AllEvents =
    [
        new PlaybackStarted(), new PlaybackPaused(), new SilenceDetected(), new ReleaseRequested(),
        new TakeRequested(), new EndpointConnected(), new EndpointDisconnected(), new AirPodsSeen(), new Tick(),
    ];

    public static TheoryData<int> Seeds() => [.. Enumerable.Range(0, 10).Select(i => i * 7919)];

    [Theory]
    [MemberData(nameof(Seeds))]
    public void La_machine_respecte_ses_regles_quelle_que_soit_la_sequence(int baseSeed)
    {
        var options = new SwitchOptions();
        for (int s = 0; s < Sequences / 10; s++)
        {
            int seed = baseSeed + s;
            var random = new Random(seed);
            var time = new FakeTimeProvider();
            var machine = new SwitchStateMachine(options, time);
            DateTimeOffset? lastSeen = null;
            DateTimeOffset? lostAt = null;
            DateTimeOffset? mutedAt = null; // haut-parleurs coupés depuis…

            for (int step = 0; step < StepsPerSequence; step++)
            {
                time.Advance(TimeSpan.FromMilliseconds(random.Next(0, 8000)));
                var e = AllEvents[random.Next(AllEvents.Length)];
                var before = machine.State;
                var commands = machine.Handle(e);
                var after = machine.State;
                string where = $"graine {seed}, étape {step} : {before} --{e.GetType().Name}--> {after} [{string.Join(", ", commands)}]";

                if (e is AirPodsSeen) lastSeen = time.GetUtcNow();
                if (after == SwitchState.Lost && before != SwitchState.Lost) lostAt = time.GetUtcNow();

                // 1. Une annonce BLE ne déclenche rien, sauf le rétablissement du son déjà dû (R7) :
                //    il sort au premier événement qui suit son échéance, quel qu'il soit.
                if (e is AirPodsSeen)
                    Assert.True(commands.All(c => c == UnmuteSpeakers) && before == after, where);

                // 2. Reconnect seulement en réponse à une lecture, une demande, ou la fin d'une relâche,
                //    et toujours en passant à Switching.
                if (commands.Contains(Reconnect))
                {
                    Assert.True(e is PlaybackStarted or TakeRequested or EndpointDisconnected, where);
                    Assert.Equal(SwitchState.Switching, after);
                }

                // 3. Une lecture ne prend pas des AirPods endormis (pas d'annonce BLE récente).
                if (e is PlaybackStarted && commands.Contains(Reconnect))
                    Assert.True(lastSeen is { } seen && time.GetUtcNow() - seen <= options.AwakeWindow, where);

                // 4. Pas de reprise automatique pendant le cooldown après une perte (anti ping-pong, R5).
                if (e is PlaybackStarted && before == SwitchState.Lost && commands.Contains(Reconnect))
                    Assert.True(time.GetUtcNow() - lostAt >= options.Cooldown, where);

                // 5. Disconnect seulement depuis OnWindows ou Switching, et toujours en passant à Releasing.
                if (commands.Contains(Disconnect))
                {
                    Assert.True(before is SwitchState.OnWindows or SwitchState.Switching, where);
                    Assert.Equal(SwitchState.Releasing, after);
                }

                // 6. PauseMedia seulement quand le PC rend les AirPods (R4) ou les perd (R6).
                if (commands.Contains(PauseMedia))
                    Assert.True(e is ReleaseRequested || after == SwitchState.Lost, where);

                // 7. Jamais deux fois la même commande d'un coup, jamais Reconnect et Disconnect ensemble.
                Assert.Equal(commands.Count, commands.Distinct().Count());
                Assert.False(commands.Contains(Reconnect) && commands.Contains(Disconnect), where);

                // 8. Un battement ne fait jamais apparaître les AirPods : seul Windows peut dire qu'ils sont là.
                if (e is Tick && after == SwitchState.OnWindows)
                    Assert.True(before is SwitchState.OnWindows or SwitchState.Releasing, where);

                // 9. On ne coupe les haut-parleurs que pendant que les AirPods arrivent, et jamais deux fois de suite.
                if (commands.Contains(MuteSpeakers))
                {
                    Assert.Equal(SwitchState.Switching, after);
                    Assert.Null(mutedAt);
                    mutedAt = time.GetUtcNow();
                }
                if (commands.Contains(UnmuteSpeakers))
                {
                    Assert.NotNull(mutedAt); // on ne rétablit que ce qu'on a coupé
                    mutedAt = null;
                }

                // 10. Le PC n'est jamais laissé muet : la coupure ne survit ni à un abandon, ni à une perte,
                //     ni à une relâche, et ne dépasse jamais MuteTimeout.
                if (mutedAt is { } since)
                {
                    Assert.True(after is SwitchState.Switching or SwitchState.OnWindows, where);
                    Assert.True(time.GetUtcNow() - since < options.MuteTimeout, where);
                }
            }
        }
    }

    [Fact]
    public void La_machine_est_deterministe()
    {
        // Même graine, même suite d'événements : exactement les mêmes commandes et états.
        Assert.Equal(Replay(42), Replay(42));
    }

    [Fact]
    public void Le_tracker_alterne_toujours_debut_et_fin_de_lecture()
    {
        var options = new SwitchOptions();
        for (int seed = 0; seed < 500; seed++)
        {
            var random = new Random(seed);
            var time = new FakeTimeProvider();
            var tracker = new AudioActivityTracker(options, time);
            bool active = false;

            for (int step = 0; step < 400; step++)
            {
                time.Advance(TimeSpan.FromMilliseconds(250));
                var events = random.Next(10) == 0
                    ? tracker.OnMediaPlaying(random.Next(2) == 0)
                    : tracker.OnPeak(random.Next(3) == 0 ? 0f : (float)random.NextDouble());

                foreach (var e in events)
                {
                    // Jamais deux débuts de suite, jamais une fin sans début.
                    Assert.True(e is PlaybackStarted ? !active : active, $"graine {seed}, étape {step} : {e}");
                    active = e is PlaybackStarted;
                }
                Assert.Equal(active, tracker.Active);
            }
        }
    }

    private static List<string> Replay(int seed)
    {
        var random = new Random(seed);
        var time = new FakeTimeProvider();
        var machine = new SwitchStateMachine(new SwitchOptions(), time);
        var trace = new List<string>();
        for (int step = 0; step < 1000; step++)
        {
            time.Advance(TimeSpan.FromMilliseconds(random.Next(0, 8000)));
            var commands = machine.Handle(AllEvents[random.Next(AllEvents.Length)]);
            trace.Add($"{machine.State} {string.Join(",", commands)}");
        }
        return trace;
    }
}
