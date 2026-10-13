namespace NotchHub.Features.Today;

/// <summary>WMO weather interpretation code (Open-Meteo) → Material Symbols icon + localized description key.</summary>
internal static class WeatherCodes
{
    private static readonly HashSet<int> Known =
    [
        0, 1, 2, 3, 45, 48, 51, 53, 55, 56, 57, 61, 63, 65, 66, 67, 71, 73, 75, 77, 80, 81, 82, 85, 86, 95, 96, 99,
    ];

    public static string Icon(int code, bool isDay) => code switch
    {
        0 or 1 => isDay ? "sunny" : "clear_night",
        2 => isDay ? "partly_cloudy_day" : "partly_cloudy_night",
        3 => "cloud",
        45 or 48 => "foggy",
        >= 51 and <= 67 => "rainy",
        >= 80 and <= 82 => "rainy",
        >= 71 and <= 77 => "weather_snowy",
        85 or 86 => "weather_snowy",
        >= 95 and <= 99 => "thunderstorm",
        _ => "cloud",
    };

    public static bool IsSun(int code, bool isDay) => isDay && code is 0 or 1;

    public static string DescriptionKey(int code) => Known.Contains(code) ? $"today.weather.{code}" : "today.weather.unknown";
}
