using System.Globalization;
using NotchHub.Core.Settings;

namespace NotchHub.Services.Weather;

/// <summary>Immutable copy of the settings the weather service depends on (value equality = change detection).</summary>
internal sealed record WeatherSettingsSnapshot(
    bool Enabled,
    string? City,
    double? Latitude,
    double? Longitude,
    string? LocationName,
    string Language)
{
    public static WeatherSettingsSnapshot From(AppSettings settings)
    {
        var weather = settings.Weather;
        return new WeatherSettingsSnapshot(
            weather.Enabled,
            Normalize(weather.City),
            weather.Latitude,
            weather.Longitude,
            Normalize(weather.LocationName),
            ResolveLanguage(settings.Language));
    }

    public bool HasCoordinates =>
        Latitude is >= -90 and <= 90 && Longitude is >= -180 and <= 180 && !(Latitude == 0 && Longitude == 0);

    /// <summary>Identifies "where" the user wants weather for; a change invalidates the shown snapshot.</summary>
    public string LocationKey =>
        HasCoordinates ? CoordinatesKey(Latitude!.Value, Longitude!.Value)
        : City is { } city ? "city:" + city.ToLowerInvariant()
        : "ip";

    /// <summary>Key of the cached snapshot: location plus language (the location name is localized).</summary>
    public string CacheKey => LocationKey + "|" + Language;

    public static string CoordinatesKey(double latitude, double longitude) =>
        string.Create(CultureInfo.InvariantCulture, $"coords:{latitude:F4},{longitude:F4}");

    /// <summary>Maps the app language setting ("pl", "en", "auto") to the language passed to Open-Meteo.</summary>
    public static string ResolveLanguage(string? appLanguage) => appLanguage?.Trim().ToLowerInvariant() switch
    {
        "pl" => "pl",
        "en" => "en",
        _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "pl" ? "pl" : "en",
    };

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
