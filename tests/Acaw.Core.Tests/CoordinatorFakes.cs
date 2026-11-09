using Acaw.Core.Config;
using Acaw.Core.Hosting;
using Microsoft.Extensions.Time.Testing;

namespace Acaw.Core.Tests;

// Faux ports du coordinateur. Le journal commun (Journal) note l'ordre exact des créations, arrêts et
// démarrages : c'est lui qui prouve « créer le nouveau → arrêter l'ancien → démarrer le nouveau ».

internal sealed class FakeConfigSource : IConfigSource
{
    public AcawConfig Stored { get; set; } = new();
    public Exception? LoadFails { get; set; }
    public int Saves { get; private set; }

    public AcawConfig Load() => LoadFails is null ? Stored : throw LoadFails;

    public void Save(AcawConfig config)
    {
        Saves++;
        Stored = config;
    }
}

internal sealed class FakeCatalog : IHeadsetCatalog
{
    public List<PairedHeadset> Paired { get; set; } = [];

    public IReadOnlyList<PairedHeadset> List() => Paired;
}

internal sealed class FakeServiceFactory(List<string> journal) : IAcawServiceFactory
{
    private int _count;

    public bool Fails { get; set; }
    public List<FakeService> Created { get; } = [];

    public IAcawService Create(AcawConfig config)
    {
        if (Fails) throw new InvalidOperationException("service audio indisponible");
        var service = new FakeService($"#{++_count} {config.Name}", config, journal);
        journal.Add($"créé {service.Label}");
        Created.Add(service);
        return service;
    }
}

/// <summary>Un vrai hôte sur de faux ports, propres à ce service (aucun mélange entre ancien et nouveau).</summary>
internal sealed class FakeService(string label, AcawConfig config, List<string> journal) : IAcawService
{
    public string Label => label;
    public AcawConfig Config => config;
    public FakeSpeakers Speakers { get; } = new();
    public bool Started { get; private set; }
    public bool Disposed { get; private set; }

    public AcawHost Host { get; } = new(config, new FakeHeadset(), new FakeMedia(), new FakeMeter(), new FakeSpeakers(),
                                        new FakeRadar(), new FakeTimeProvider(), _ => { });

    public void Start()
    {
        journal.Add($"démarré {label}");
        Started = true;
        Host.Start(background: false);
    }

    public void Dispose()
    {
        Host.Dispose();
        Speakers.Unmute(); // comme le vrai : l'hôte arrêté ne peut plus recouper le son
        Disposed = true;
        journal.Add($"arrêté {label}");
    }
}
