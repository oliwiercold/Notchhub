namespace NotchHub.Features.Compact;

internal static class Glyphs
{
    /// <summary>WMO weather code → Material Symbols name.</summary>
    public static string Weather(int code, bool isDay) => code switch
    {
        0 => isDay ? "sunny" : "clear_night",
        1 or 2 => isDay ? "partly_cloudy_day" : "partly_cloudy_night",
        3 => "cloud",
        45 or 48 => "foggy",
        (>= 51 and <= 67) or (>= 80 and <= 82) => "rainy",
        (>= 71 and <= 77) or 85 or 86 => "weather_snowy",
        >= 95 => "thunderstorm",
        _ => "cloud",
    };

    public static string Battery(int percent, bool charging) =>
        charging ? "battery_charging_full"
        : percent >= 95 ? "battery_full"
        : $"battery_{Math.Clamp((int)Math.Round(percent / 100.0 * 6), 0, 6)}_bar";
}
