using System.Runtime.InteropServices;
using Windows.Win32.Foundation;

namespace Acaw.Windows;

/// <summary>Petits utilitaires pour les appels COM.</summary>
internal static class Com
{
    /// <summary>Copie une chaîne allouée par Windows puis libère la mémoire (sinon : fuite).</summary>
    public static unsafe string TakeString(PWSTR ptr)
    {
        try { return ptr.ToString(); }
        finally { Marshal.FreeCoTaskMem((nint)ptr.Value); }
    }

    /// <summary>
    /// Erreurs « normales » d'un appel COM : COMException, ou InvalidCastException quand Windows répond
    /// E_NOINTERFACE (« je n'ai pas cette interface »).
    /// </summary>
    public static bool IsComFailure(Exception e) => e is COMException or InvalidCastException;
}
