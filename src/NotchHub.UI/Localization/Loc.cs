using System.Globalization;
using System.Windows;
using NotchHub.Core;

namespace NotchHub.UI.Localization;

/// <summary>
/// Runtime-switchable localization (Polish / English).
/// Every assembly that has strings ships "Strings/pl.xaml" and "Strings/en.xaml" (ResourceDictionary of sys:String,
/// keys prefixed with the feature name, e.g. "controls.wifi"). The App registers the assemblies, then calls SetLanguage.
/// XAML: Text="{DynamicResource controls.wifi}"  ·  code: Loc.Get("controls.wifi"), Loc.Format("stats.procs", 229).
/// </summary>
public static class Loc
{
    private static readonly List<string> Assemblies = ["NotchHub.UI"];
    private static readonly List<ResourceDictionary> Active = [];
    private static readonly HashSet<string> Missing = [];

    public static string Language { get; private set; } = "pl";
    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("pl-PL");
    public static event EventHandler? LanguageChanged;

    /// <summary>Registers an assembly containing Strings/{lang}.xaml. Call before <see cref="SetLanguage"/>.</summary>
    public static void Register(string assemblyName)
    {
        if (!Assemblies.Contains(assemblyName)) Assemblies.Add(assemblyName);
    }

    /// <summary>"pl", "en" or "auto" (Windows UI language: Polish → pl, everything else → en).</summary>
    public static void SetLanguage(string language)
    {
        var lang = language switch
        {
            "pl" or "en" => language,
            _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "pl" ? "pl" : "en",
        };
        var app = Application.Current;
        if (app is null) return;

        foreach (var d in Active) app.Resources.MergedDictionaries.Remove(d);
        Active.Clear();
        foreach (var asm in Assemblies)
        {
            try
            {
                var dict = new ResourceDictionary { Source = new Uri($"pack://application:,,,/{asm};component/Strings/{lang}.xaml", UriKind.Absolute) };
                app.Resources.MergedDictionaries.Add(dict);
                Active.Add(dict);
            }
            catch (Exception ex)
            {
                Log.Warn($"Loc: no strings for {asm} ({lang})", ex);
            }
        }

        Language = lang;
        Culture = CultureInfo.GetCultureInfo(lang == "pl" ? "pl-PL" : "en-GB");
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Localized string for <paramref name="key"/>; the key itself when missing (logged once).</summary>
    public static string Get(string key)
    {
        if (Application.Current?.TryFindResource(key) is string s) return s;
        lock (Missing)
        {
            if (Missing.Add(key)) Log.Warn($"Loc: missing string '{key}' ({Language})");
        }
        return key;
    }

    /// <summary>string.Format with the current culture on the localized template.</summary>
    public static string Format(string key, params object?[] args)
    {
        try { return string.Format(Culture, Get(key), args); }
        catch (FormatException) { return Get(key); }
    }
}
