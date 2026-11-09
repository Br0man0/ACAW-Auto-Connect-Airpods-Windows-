using Acaw.Core.Config;
using Acaw.Core.Hosting;
using Acaw.Core.Switching;

namespace Acaw.Core.Tests;

/// <summary>
/// Invariants du coordinateur de configuration : des séquences aléatoires (graine fixe) de fichiers modifiés,
/// abîmés, verrouillés ou supprimés, de casques qui apparaissent et disparaissent, de créations qui échouent
/// et de clics dans le menu. Après chaque opération, des règles qui doivent TOUJOURS tenir.
/// </summary>
public class ConfigCoordinatorInvariantTests
{
    private static readonly PairedHeadset Mine = new("A1:B2:C3:D4:E5:F6", "2027", "AirPods de Romsy", IsApple: true);
    private static readonly PairedHeadset Lou = new("0A:1B:2C:3D:4E:5F", "2014", "AirPods de Lou", IsApple: true);
    private static readonly PairedHeadset Speaker = new("11:22:33:44:55:66", "0023", "Enceinte JBL", IsApple: false);
    private static readonly PairedHeadset[] All = [Mine, Lou, Speaker];

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Le_coordinateur_respecte_ses_regles_quelle_que_soit_la_sequence(int baseSeed)
    {
        for (int s = 0; s < 250; s++)
        {
            int seed = baseSeed * 10_000 + s;
            var random = new Random(seed);
            var journal = new List<string>();
            var file = new FakeConfigSource();
            var catalog = new FakeCatalog();
            var factory = new FakeServiceFactory(journal);
            using var coordinator = new ConfigCoordinator(file, catalog, factory, _ => { });

            for (int step = 0; step < 60; step++)
            {
                string op = RandomOperation(random, file, catalog, factory, coordinator);
                string where = $"graine {seed}, étape {step} : {op}";
                int savesBefore = file.Saves;
                bool fileBroken = file.LoadFails is InvalidDataException;
                int severalBefore = catalog.Paired.Count(AirPodsPicker.IsCandidate);
                bool configuredBefore = AirPodsPicker.IsUsable(file.Stored);

                Run(op, random, coordinator);

                var running = factory.Created.Where(c => c.Started && !c.Disposed).ToList();

                // 1. Jamais deux hôtes en marche en même temps.
                Assert.True(running.Count <= 1, where);

                // 2. L'hôte exposé est exactement celui qui tourne.
                Assert.Equal(running.SingleOrDefault()?.Host, coordinator.Host);

                // 3. Un hôte n'a démarré qu'après l'arrêt de tous ceux d'avant (créer → arrêter l'ancien → démarrer).
                foreach (var started in factory.Created.Where(c => c.Started))
                {
                    int startIndex = journal.IndexOf($"démarré {started.Label}");
                    foreach (var earlier in factory.Created.TakeWhile(c => c != started).Where(c => c.Started))
                        Assert.True(journal.IndexOf($"arrêté {earlier.Label}") is >= 0 and var stop && stop < startIndex, where);
                }

                // 4. L'hôte en marche tourne avec une configuration utilisable, celle qu'annonce le coordinateur
                //    (au nom et à la bascule près, qui s'appliquent sans recréer l'hôte).
                if (running.SingleOrDefault() is { } service)
                {
                    Assert.True(AirPodsPicker.IsUsable(service.Config), where);
                    Assert.Equal(ConfigChange.Nothing,
                                 ConfigChanges.Compare(service.Config with { AutoSwitch = coordinator.Config!.AutoSwitch }, coordinator.Config));
                    Assert.Null(coordinator.Problem);
                }

                // 5. Un fichier abîmé n'est jamais écrasé par une simple relecture.
                if (op == "relire" && fileBroken) Assert.Equal(savesBefore, file.Saves);

                // 6. La sélection automatique ne choisit jamais au hasard entre plusieurs paires.
                if (op == "relire" && !configuredBefore && severalBefore > 1 && !fileBroken)
                    Assert.Equal(savesBefore, file.Saves);
            }
        }
    }

    // Prépare le monde (fichier, casques, fabrique) puis choisit l'opération à faire.
    private static string RandomOperation(Random random, FakeConfigSource file, FakeCatalog catalog,
                                          FakeServiceFactory factory, ConfigCoordinator coordinator)
    {
        catalog.Paired = random.Next(6) == 0 ? [] : All.Where(_ => random.Next(2) == 0).ToList();
        factory.Fails = random.Next(5) == 0;
        file.LoadFails = random.Next(8) switch
        {
            0 => new InvalidDataException("Configuration illisible"),
            1 => new IOException("verrouillé"),
            _ => null,
        };
        switch (random.Next(5))
        {
            case 0: file.Stored = new AcawConfig(); break; // fichier supprimé
            case 1: file.Stored = new AcawConfig().WithHeadset(random.Next(2) == 0 ? Mine : Lou); break;
            case 2: file.Stored = file.Stored with { Switching = new SwitchOptions { PauseDelay = TimeSpan.FromSeconds(random.Next(1, 4)) } }; break;
            case 3: file.Stored = file.Stored with { AutoSwitch = random.Next(2) == 0, Name = $"nom {random.Next(3)}" }; break;
        }
        return random.Next(6) switch
        {
            0 when coordinator.Candidates().Count > 0 => "choisir",
            1 => "bascule",
            _ => "relire",
        };
    }

    private static void Run(string op, Random random, ConfigCoordinator coordinator)
    {
        switch (op)
        {
            case "choisir":
                var candidates = coordinator.Candidates();
                coordinator.Choose(candidates[random.Next(candidates.Count)]);
                break;
            case "bascule":
                coordinator.SetAutoSwitch(random.Next(2) == 0);
                break;
            default:
                coordinator.Refresh();
                break;
        }
    }
}
