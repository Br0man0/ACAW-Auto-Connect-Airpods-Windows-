using Acaw.Core.Config;
using Acaw.Core.Hosting;

namespace Acaw.Windows.Bluetooth;

/// <summary>Le port « casques appairés » du coordinateur : les endpoints musique (A2DP) Bluetooth de Windows.</summary>
public sealed class WindowsHeadsetCatalog : IHeadsetCatalog
{
    public IReadOnlyList<PairedHeadset> List()
    {
        try
        {
            // Les objets COM audio sont créés hors du thread de l'interface (règle du projet, ADR-0002) :
            // le menu de l'icône appelle cette méthode depuis le thread STA.
            return Task.Run(() => BluetoothAudio.ListEndpoints()
                    .Where(ep => ep.IsA2dp && ep.Mac is not null)
                    .Select(ep => new PairedHeadset(ep.Mac!, ep.ModelId, ep.Name, ep.IsApple))
                    .ToList())
                .GetAwaiter().GetResult();
        }
        catch (Exception e) when (Com.IsComFailure(e))
        {
            return []; // Windows ne répond pas (service audio pas prêt) : le coordinateur réessaiera
        }
    }
}
