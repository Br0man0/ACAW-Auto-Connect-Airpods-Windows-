using System.Runtime.InteropServices;

namespace Acaw.Service;

/// <summary>
/// Prévient quand Windows veut retirer l'adaptateur ouvert par le service (Gestionnaire de périphériques, mise à
/// jour du pilote, désactivation…), pour fermer aussitôt la poignée.
/// </summary>
/// <remarks>
/// Une poignée ouverte bloque le retrait : sans cette surveillance, « Mettre à jour le pilote » dans le
/// Gestionnaire de périphériques (la procédure de secours) échouerait ou exigerait un redémarrage tant que le
/// service tient l'adaptateur. Windows attend la réponse du rappel : la poignée est donc fermée avant de répondre
/// (au plus 10 s).
/// </remarks>
internal sealed unsafe partial class RemovalWatch : IDisposable
{
    // CM_NOTIFY_ACTION : demande de retrait, retrait en cours.
    private const int QueryRemove = 2;
    private const int RemovePending = 4;

    private readonly Action _release;
    private GCHandle _self;
    private nint _notify;

    private RemovalWatch(Action release) => _release = release;

    /// <summary>
    /// Surveille le périphérique ouvert par <paramref name="file"/> ; <paramref name="release"/> doit fermer la
    /// poignée (relais compris) avant de rendre la main.
    /// </summary>
    public static RemovalWatch Start(SafeHandle file, Action release)
    {
        var watch = new RemovalWatch(release);
        watch._self = GCHandle.Alloc(watch);
        // CM_NOTIFY_FILTER (416 octets) : cbSize, Flags, FilterType (1 = poignée de périphérique), Reserved,
        // puis la poignée surveillée.
        const int size = 416;
        byte* filter = stackalloc byte[size];
        new Span<byte>(filter, size).Clear();
        *(uint*)filter = size;
        *(uint*)(filter + 8) = 1;
        *(nint*)(filter + 16) = file.DangerousGetHandle();
        uint result = CM_Register_Notification(filter, GCHandle.ToIntPtr(watch._self), &OnNotify, out watch._notify);
        if (result != 0)
        {
            ServiceLog.Warn("pilote", $"retrait de l'adaptateur non surveillé (CONFIGRET {result})");
            watch.Dispose();
        }
        return watch;
    }

    public void Dispose()
    {
        if (_notify != 0)
        {
            CM_Unregister_Notification(_notify);
            _notify = 0;
        }
        if (_self.IsAllocated) _self.Free();
    }

    [UnmanagedCallersOnly]
    private static uint OnNotify(nint notify, nint context, int action, nint data, uint size)
    {
        if (action is QueryRemove or RemovePending && GCHandle.FromIntPtr(context).Target is RemovalWatch watch)
        {
            ServiceLog.Info("pilote", "Windows veut retirer l'adaptateur : le service le lâche");
            var done = new ManualResetEventSlim();
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    watch._release();
                }
                finally
                {
                    done.Set();
                }
            });
            done.Wait(TimeSpan.FromSeconds(10));
        }
        return 0; // ERROR_SUCCESS : le retrait peut continuer
    }

    [LibraryImport("cfgmgr32.dll")]
    private static partial uint CM_Register_Notification(void* filter, nint context,
        delegate* unmanaged<nint, nint, int, nint, uint, uint> callback, out nint notify);

    [LibraryImport("cfgmgr32.dll")]
    private static partial uint CM_Unregister_Notification(nint notify);
}
