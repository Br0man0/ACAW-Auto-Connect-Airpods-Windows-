using System.Reflection;

namespace Acaw.Core;

/// <summary>Informations sur ACAW lui-même.</summary>
public static class AppInfo
{
    /// <summary>
    /// La version, fixée une seule fois dans Directory.Build.props (ex. « 0.4.0 »). Affichée par
    /// « acaw --version », par le menu de l'icône et au début de chaque lancement dans le journal.
    /// </summary>
    public static string Version { get; } =
        Clean(typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    /// <summary>
    /// .NET ajoute l'empreinte du commit à la version (« 0.4.0+8090b1e… ») : utile pour un débogueur,
    /// illisible pour un utilisateur. On ne garde que la partie avant le « + ».
    /// </summary>
    public static string Clean(string? informationalVersion) =>
        string.IsNullOrWhiteSpace(informationalVersion) ? "?" : informationalVersion.Split('+')[0];
}
