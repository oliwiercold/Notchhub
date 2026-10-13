// Live smoke test of WeatherService against Open-Meteo, using a throw-away data root.
using NotchHub.Core;
using NotchHub.Core.Settings;
using NotchHub.Services.Weather;

var root = Path.Combine(Path.GetTempPath(), "notchhub-harness-today");
AppPaths.Initialize(root);
var settings = new SettingsService(AppPaths.File("settings.json"));
settings.Update(s => { s.Weather.City = args.Length > 0 ? args[0] : "Warszawa"; s.Weather.Latitude = null; s.Weather.Longitude = null; });

using var weather = new WeatherService(settings);
var search = await weather.SearchLocationAsync("Krak");
Console.WriteLine("search: " + string.Join("; ", search.Select(g => $"{g.Name}/{g.Region}/{g.Country} {g.Latitude:F2},{g.Longitude:F2}")));
await weather.StartAsync();
await weather.RefreshAsync();
var w = weather.Current;
Console.WriteLine($"error={weather.LastError} loc={w?.LocationName} t={w?.TemperatureC} feels={w?.ApparentTemperatureC} code={w?.WeatherCode} day={w?.IsDay} hum={w?.HumidityPercent} wind={w?.WindKmh} min/max={w?.TodayMinC}/{w?.TodayMaxC}");
Console.WriteLine($"hourly={w?.Hourly.Count} first={w?.Hourly.FirstOrDefault()} daily={w?.Daily.Count} d0={w?.Daily.FirstOrDefault()}");
Console.WriteLine($"settings coords={settings.Current.Weather.Latitude},{settings.Current.Weather.Longitude} name={settings.Current.Weather.LocationName}");
Console.WriteLine("cache exists: " + File.Exists(AppPaths.File("weather-cache.json")));
