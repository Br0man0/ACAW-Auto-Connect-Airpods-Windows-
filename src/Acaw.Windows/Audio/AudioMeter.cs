using Acaw.Core.Hosting;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Media.Audio;
using Windows.Win32.Media.Audio.Endpoints;
using Windows.Win32.System.Com;

namespace Acaw.Windows.Audio;

/// <summary>
/// Le port « volume » de l'hôte : volume crête des applications qui jouent sur la sortie par défaut,
/// sans compter les sons système (notifications de Windows). Utilisé uniquement depuis le thread de l'hôte.
/// </summary>
public sealed class AudioMeter : ISoundMeter
{
    private readonly IMMDeviceEnumerator _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
    private string? _deviceId;
    private IAudioSessionManager2? _sessions;

    public float ReadPeak()
    {
        try
        {
            RefreshDefaultDevice();
            var list = _sessions!.GetSessionEnumerator();
            list.GetCount(out int count);

            float max = 0;
            for (int i = 0; i < count; i++)
            {
                list.GetSession(i, out var session);
                // S_OK (0) = c'est la session des sons système : on l'ignore. S_FALSE (1) = une application.
                if (((IAudioSessionControl2)session).IsSystemSoundsSession().Value == 0) continue;
                ((IAudioMeterInformation)session).GetPeakValue(out float peak);
                max = Math.Max(max, peak);
            }
            return max;
        }
        catch (Exception e) when (Com.IsComFailure(e))
        {
            _deviceId = null; // aucune sortie son, ou elle vient de changer : on réessaiera au prochain relevé
            return 0;
        }
    }

    // La sortie par défaut change quand les AirPods arrivent ou repartent : on suit le mouvement en
    // comparant son identifiant à chaque relevé (un appel très léger).
    private void RefreshDefaultDevice()
    {
        _enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out var device);
        device.GetId(out PWSTR idPtr);
        string id = Com.TakeString(idPtr);
        if (id == _deviceId && _sessions is not null) return;

        device.Activate(typeof(IAudioSessionManager2).GUID, CLSCTX.CLSCTX_ALL, null, out object manager);
        _sessions = (IAudioSessionManager2)manager;
        _deviceId = id;
    }
}
