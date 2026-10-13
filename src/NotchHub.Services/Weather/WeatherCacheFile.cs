using NotchHub.Core.Contracts;

namespace NotchHub.Services.Weather;

/// <summary>
/// Content of weather-cache.json: the last snapshot (shown instantly at startup) plus what the service remembers
/// about locations (IP lookup cached for a day, provenance of coordinates it geocoded into the settings).
/// </summary>
internal sealed class WeatherCacheFile
{
    public int Version { get; set; } = 1;

    /// <summary><see cref="WeatherSettingsSnapshot.CacheKey"/> the snapshot was fetched for.</summary>
    public string? SnapshotKey { get; set; }

    public WeatherSnapshot? Snapshot { get; set; }

    /// <summary>Set when the service geocoded <see cref="GeocodedCity.City"/> and wrote the coordinates into the settings.</summary>
    public GeocodedCity? Geocoded { get; set; }

    public IpLocation? Ip { get; set; }
}

/// <summary>Provenance of coordinates the service geocoded from a city name and persisted into the settings.</summary>
internal sealed record GeocodedCity(string City, string Language, double Latitude, double Longitude);

/// <summary>Approximate location derived from the public IP address (LocalizedName: optional display name in NameLanguage).</summary>
internal sealed record IpLocation(
    double Latitude,
    double Longitude,
    string? City,
    string? Region,
    string? Country,
    DateTimeOffset ResolvedAt,
    string? LocalizedName = null,
    string? NameLanguage = null)
{
    public string DisplayName => LocalizedName ?? City ?? Region ?? Country ?? "";
}
