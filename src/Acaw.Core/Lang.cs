using System.Globalization;

namespace Acaw.Core;

/// <summary>Les langues d'ACAW.</summary>
public enum AppLanguage
{
    French,
    English,
}

/// <summary>
/// La langue des textes d'ACAW (menu, notifications, ligne de commande, journal). Chaque texte est écrit dans les
/// deux langues à l'endroit où il sert : <c>Lang.Pick("Amener sur ce PC", "Bring to this PC")</c>. Avec deux
/// langues, c'est le plus simple à lire, et une traduction ne peut pas manquer : le compilateur exige les deux.
/// La langue vient du réglage <c>Language</c> de config.json (« fr », « en »), ou de celle de Windows s'il est vide.
/// </summary>
public static class Lang
{
    private static volatile int _current = (int)AppLanguage.French;

    /// <summary>La langue en cours. Lisible depuis n'importe quel thread.</summary>
    public static AppLanguage Current
    {
        get => (AppLanguage)_current;
        set
        {
            if ((int)value == _current) return;
            _current = (int)value;
            Changed?.Invoke();
        }
    }

    /// <summary>La langue vient de changer (menu Langue, ou config.json modifié) : les textes affichés sont à refaire.</summary>
    public static event Action? Changed;

    /// <summary>Le texte dans la langue en cours.</summary>
    public static string Pick(string french, string english) => Current == AppLanguage.English ? english : french;

    /// <summary>
    /// La langue d'un réglage : « fr » ou « en » (sans tenir compte des majuscules) ; vide ou inconnu → celle de Windows.
    /// </summary>
    public static AppLanguage Resolve(string? setting, CultureInfo? windows = null) => setting?.Trim().ToLowerInvariant() switch
    {
        "fr" => AppLanguage.French,
        "en" => AppLanguage.English,
        _ => FromWindows(windows),
    };

    /// <summary>Windows en français → français ; toute autre langue → anglais.</summary>
    public static AppLanguage FromWindows(CultureInfo? culture = null) =>
        (culture ?? CultureInfo.CurrentUICulture).TwoLetterISOLanguageName == "fr" ? AppLanguage.French : AppLanguage.English;

    /// <summary>Le code enregistré dans config.json : « fr » ou « en ».</summary>
    public static string Code(AppLanguage language) => language == AppLanguage.English ? "en" : "fr";
}
