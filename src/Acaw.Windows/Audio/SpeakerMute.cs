using Acaw.Core.Hosting;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Media.Audio;
using Windows.Win32.Media.Audio.Endpoints;
using Windows.Win32.System.Com;

namespace Acaw.Windows.Audio;

/// <summary>
/// Le port « sourdine » de l'hôte (règle R7) : coupe la sortie par défaut pendant que les AirPods arrivent.
/// Deux sécurités, pour ne jamais laisser le PC muet :
/// <list type="bullet">
/// <item>on ne rétablit que la sortie que NOUS avons coupée (une sortie déjà coupée par l'utilisateur n'est pas touchée) ;</item>
/// <item>la sortie coupée est notée dans un fichier ; si ACAW s'arrête brutalement, le prochain démarrage la rétablit.</item>
/// </list>
/// Utilisé uniquement depuis le thread de l'hôte.
/// </summary>
public sealed class SpeakerMute : ISpeakers
{
    private readonly IMMDeviceEnumerator _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
    private readonly string _markerPath;

    /// <param name="markerPath">Fichier mémorisant la sortie coupée (par défaut dans %LOCALAPPDATA%\ACAW).</param>
    public SpeakerMute(string? markerPath = null)
    {
        _markerPath = markerPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ACAW", "speakers-muted.txt");
    }

    public void Mute()
    {
        _enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out var device);
        device.GetId(out PWSTR idPtr);
        string id = Com.TakeString(idPtr);

        var volume = Volume(device);
        volume.GetMute(out BOOL alreadyMuted);
        if (alreadyMuted) return; // coupée par l'utilisateur : ce n'est pas à nous de la rétablir

        // Noter AVANT de couper : si on s'arrête juste après, le prochain démarrage saura quoi rétablir.
        Directory.CreateDirectory(Path.GetDirectoryName(_markerPath)!);
        File.WriteAllText(_markerPath, id);
        SetMute(volume, true);
    }

    public void Unmute()
    {
        if (!File.Exists(_markerPath)) return; // rien n'a été coupé par nous
        string id = File.ReadAllText(_markerPath).Trim();
        try
        {
            _enumerator.GetDevice(id, out var device);
            SetMute(Volume(device), false);
        }
        catch (Exception e) when (Com.IsComFailure(e))
        {
            // La sortie a disparu (casque USB débranché…) : il n'y a plus rien à rétablir.
        }
        File.Delete(_markerPath);
    }

    private static IAudioEndpointVolume Volume(IMMDevice device)
    {
        device.Activate(typeof(IAudioEndpointVolume).GUID, CLSCTX.CLSCTX_ALL, null, out object volume);
        return (IAudioEndpointVolume)volume;
    }

    private static unsafe void SetMute(IAudioEndpointVolume volume, bool mute) => volume.SetMute(mute, null);
}
