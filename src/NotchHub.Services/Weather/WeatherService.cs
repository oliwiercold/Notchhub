using System.Globalization;
using System.Text.Json;
using NotchHub.Core;
using NotchHub.Core.Contracts;
using NotchHub.Core.Settings;
using NotchHub.Services.Common;

namespace NotchHub.Services.Weather;

/// <summary>
/// Open-Meteo weather. Location priority: coordinates from the settings, else the settings city (geocoded once and
/// persisted into the settings), else the public-IP location (cached for a day). Refreshes every 30 minutes, right
/// after relevant settings changes, and retries failures with backoff. The last snapshot is cached in
/// weather-cache.json and shown immediately at start-up. With Weather.Enabled = false nothing is requested.
/// Error codes in <see cref="LastError"/>: "http_&lt;status&gt;", "timeout", "network", "parse", "location_not_found", "unknown".
/// </summary>
public sealed class WeatherService : IWeatherService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan IpCacheLifetime = TimeSpan.FromDays(1);
    private static readonly TimeSpan[] Backoff =
        [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30)];
    private static readonly HttpClient SharedHttp = ServiceHttp.Create(TimeSpan.FromSeconds(20));

    private const string ForecastFields =
        "&current=temperature_2m,apparent_temperature,relative_humidity_2m,weather_code,is_day,wind_speed_10m" +
        "&hourly=temperature_2m,weather_code,is_day,precipitation_probability" +
        "&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max,sunrise,sunset" +
        "&timezone=auto&forecast_days=7&wind_speed_unit=kmh";

    private readonly ISettingsService _settings;
    private readonly HttpClient _http;
    private readonly TimeProvider _time;
    private readonly string _cachePath;
    private readonly object _gate = new();
    private readonly AsyncSignal _wake = new();
    private readonly SemaphoreSlim _fetchGate = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private WeatherCacheFile _cache = new();
    private WeatherSettingsSnapshot _lastSettings;
    private WeatherSnapshot? _current;
    private string? _currentKey;
    private bool _isLoading;
    private string? _lastError;
    private int _failures;
    private bool _started;
    private bool _disposed;

    public WeatherService(ISettingsService settings)
        : this(settings, AppPaths.File("weather-cache.json"), SharedHttp, TimeProvider.System) { }

    internal WeatherService(ISettingsService settings, string cachePath, HttpClient http, TimeProvider time)
    {
        _settings = settings;
        _cachePath = cachePath;
        _http = http;
        _time = time;
        _lastSettings = WeatherSettingsSnapshot.From(settings.Current);
    }

    public WeatherSnapshot? Current { get { lock (_gate) return _current; } }
    public bool IsLoading { get { lock (_gate) return _isLoading; } }
    public string? LastError { get { lock (_gate) return _lastError; } }

    public event EventHandler? Changed;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_started || _disposed) return Task.CompletedTask;
            _started = true;
        }

        var cache = JsonStore.Load(_cachePath, () => new WeatherCacheFile());
        var settings = WeatherSettingsSnapshot.From(_settings.Current);
        bool restored;
        lock (_gate)
        {
            _cache = cache;
            _lastSettings = settings;
            restored = settings.Enabled && cache.Snapshot is not null && cache.SnapshotKey == CacheKey(settings);
            if (restored)
            {
                _current = cache.Snapshot;
                _currentKey = cache.SnapshotKey;
            }
        }
        if (restored) RaiseChanged();

        _settings.Changed += OnSettingsChanged;
        RunLoopAsync(_cts.Token).Forget("WeatherService.loop");
        return Task.CompletedTask;
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        var settings = WeatherSettingsSnapshot.From(_settings.Current);
        if (!settings.Enabled) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
        await FetchAsync(settings, linked.Token).ConfigureAwait(false);
        _wake.Set(); // let the loop re-plan its next refresh
    }

    public async Task<IReadOnlyList<GeoLocation>> SearchLocationAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2) return [];
        try
        {
            var language = WeatherSettingsSnapshot.ResolveLanguage(_settings.Current.Language);
            return await GeocodeAsync(query.Trim(), language, 8, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            Log.Warn($"WeatherService: location search failed ({ServiceHttp.ErrorCode(ex, ct)})");
            return [];
        }
    }

    // ───────────────────────────── scheduling ─────────────────────────────

    private async Task RunLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var settings = WeatherSettingsSnapshot.From(_settings.Current);
                TimeSpan wait;
                if (!settings.Enabled)
                {
                    wait = Timeout.InfiniteTimeSpan; // woken by a settings change
                }
                else
                {
                    wait = DueIn(settings);
                    if (wait <= TimeSpan.Zero)
                    {
                        var ok = await FetchAsync(settings, ct).ConfigureAwait(false);
                        int failures;
                        lock (_gate) failures = _failures;
                        wait = ok ? RefreshInterval : Backoff[Math.Clamp(failures - 1, 0, Backoff.Length - 1)];
                    }
                }
                await _wake.WaitAsync(wait, _time, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    private TimeSpan DueIn(WeatherSettingsSnapshot settings)
    {
        lock (_gate)
        {
            if (_current is null || _currentKey != CacheKey(settings)) return TimeSpan.Zero;
            return RefreshInterval - (_time.GetUtcNow() - _current.FetchedAt);
        }
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        var settings = WeatherSettingsSnapshot.From(_settings.Current);
        bool disabledNow;
        lock (_gate)
        {
            if (settings == _lastSettings) return;
            _lastSettings = settings;
            _failures = 0;
            disabledNow = !settings.Enabled && (_current is not null || _lastError is not null);
            if (!settings.Enabled)
            {
                _current = null;
                _currentKey = null;
                _lastError = null;
            }
        }
        if (disabledNow) RaiseChanged();
        _wake.Set();
    }

    // ───────────────────────────── location ─────────────────────────────

    private sealed record ResolvedLocation(double Latitude, double Longitude, string Name, string Key);

    private sealed class LocationNotFoundException() : Exception("Location not found.");

    /// <summary>
    /// True when the settings coordinates were written by this service for a different city than the one now
    /// in the settings (the user changed the city), so they must be geocoded again.
    /// </summary>
    private bool IsStaleGeocode(WeatherSettingsSnapshot s)
    {
        var geocoded = _cache.Geocoded;
        return geocoded is not null && s.HasCoordinates
            && Math.Abs(geocoded.Latitude - s.Latitude!.Value) < 1e-4
            && Math.Abs(geocoded.Longitude - s.Longitude!.Value) < 1e-4
            && !string.Equals(geocoded.City, s.City, StringComparison.CurrentCultureIgnoreCase);
    }

    private string EffectiveLocationKey(WeatherSettingsSnapshot s) =>
        s.HasCoordinates && !IsStaleGeocode(s) ? s.LocationKey
        : s.City is { } city ? "city:" + city.ToLowerInvariant()
        : "ip";

    private string CacheKey(WeatherSettingsSnapshot s) => EffectiveLocationKey(s) + "|" + s.Language;

    private async Task<ResolvedLocation> ResolveLocationAsync(WeatherSettingsSnapshot s, CancellationToken ct)
    {
        if (s.HasCoordinates && !IsStaleGeocode(s))
            return new(s.Latitude!.Value, s.Longitude!.Value, s.LocationName ?? s.City ?? "", s.LocationKey);

        if (s.City is { } city)
        {
            var found = await GeocodeAsync(city, s.Language, 1, ct).ConfigureAwait(false);
            var best = found.Count > 0 ? found[0] : throw new LocationNotFoundException();
            var lat = Math.Round(best.Latitude, 4);
            var lon = Math.Round(best.Longitude, 4);
            lock (_gate) _cache.Geocoded = new GeocodedCity(city, s.Language, lat, lon);
            // Persist so later starts skip geocoding; the resulting Changed event is recognised by the key match.
            _settings.Update(a =>
            {
                a.Weather.Latitude = lat;
                a.Weather.Longitude = lon;
                a.Weather.LocationName = best.Name;
            });
            return new(lat, lon, best.Name, WeatherSettingsSnapshot.CoordinatesKey(lat, lon));
        }

        var ip = _cache.Ip;
        if (ip is null || _time.GetUtcNow() - ip.ResolvedAt > IpCacheLifetime)
        {
            ip = await LookupIpAsync(ct).ConfigureAwait(false);
            lock (_gate) _cache.Ip = ip;
        }
        return new(ip.Latitude, ip.Longitude, ip.DisplayName, "ip");
    }

    private async Task<IpLocation> LookupIpAsync(CancellationToken ct)
    {
        using var doc = await GetJsonAsync("https://get.geojs.io/v1/ip/geo.json", ct).ConfigureAwait(false);
        var root = doc.RootElement;
        var lat = ParseNumber(root, "latitude");
        var lon = ParseNumber(root, "longitude");
        if (double.IsNaN(lat) || double.IsNaN(lon)) throw new LocationNotFoundException();
        return new IpLocation(lat, lon, GetString(root, "city"), GetString(root, "region"), GetString(root, "country"), _time.GetUtcNow());
    }

    private async Task<IReadOnlyList<GeoLocation>> GeocodeAsync(string query, string language, int count, CancellationToken ct)
    {
        var url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(query)}&count={count}&language={language}&format=json";
        using var doc = await GetJsonAsync(url, ct).ConfigureAwait(false);
        if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array) return [];
        var list = new List<GeoLocation>();
        foreach (var r in results.EnumerateArray())
        {
            var name = GetString(r, "name");
            if (name is null || !r.TryGetProperty("latitude", out var lat) || !r.TryGetProperty("longitude", out var lon)) continue;
            list.Add(new GeoLocation(name, GetString(r, "admin1"), GetString(r, "country"), lat.GetDouble(), lon.GetDouble()));
        }
        return list;
    }

    // ───────────────────────────── forecast ─────────────────────────────

    private async Task<bool> FetchAsync(WeatherSettingsSnapshot settings, CancellationToken ct)
    {
        await _fetchGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            SetLoading(true);
            var location = await ResolveLocationAsync(settings, ct).ConfigureAwait(false);
            var snapshot = await FetchForecastAsync(location, ct).ConfigureAwait(false);
            var key = location.Key + "|" + settings.Language;
            WeatherCacheFile toSave;
            lock (_gate)
            {
                _current = snapshot;
                _currentKey = key;
                _lastError = null;
                _failures = 0;
                _cache.Snapshot = snapshot;
                _cache.SnapshotKey = key;
                toSave = _cache;
            }
            JsonStore.Save(_cachePath, toSave);
            return true;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            var code = ex is LocationNotFoundException ? "location_not_found" : ServiceHttp.ErrorCode(ex, ct);
            if (ServiceHttp.IsExpectedNetworkFailure(ex) || ex is LocationNotFoundException) Log.Warn($"WeatherService: refresh failed ({code})");
            else Log.Warn($"WeatherService: refresh failed ({code})", ex);
            lock (_gate)
            {
                _lastError = code;
                _failures++;
            }
            return false;
        }
        finally
        {
            SetLoading(false);
            _fetchGate.Release();
        }
    }

    private async Task<WeatherSnapshot> FetchForecastAsync(ResolvedLocation location, CancellationToken ct)
    {
        var url = string.Create(CultureInfo.InvariantCulture,
            $"https://api.open-meteo.com/v1/forecast?latitude={location.Latitude:0.####}&longitude={location.Longitude:0.####}") + ForecastFields;
        using var doc = await GetJsonAsync(url, ct).ConfigureAwait(false);
        return ParseForecast(doc.RootElement, location.Name, _time.GetUtcNow());
    }

    /// <summary>Parses an Open-Meteo forecast response (timezone=auto, local times without offset).</summary>
    internal static WeatherSnapshot ParseForecast(JsonElement root, string locationName, DateTimeOffset now)
    {
        var offset = TimeSpan.FromSeconds(root.TryGetProperty("utc_offset_seconds", out var o) ? o.GetInt32() : 0);
        DateTimeOffset ParseTime(string text) => new(
            DateTime.ParseExact(text, ["yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None),
            offset);

        var current = root.GetProperty("current");

        var hourly = new List<WeatherHour>(24);
        var h = root.GetProperty("hourly");
        var hTimes = h.GetProperty("time");
        for (var i = 0; i < hTimes.GetArrayLength() && hourly.Count < 24; i++)
        {
            var time = ParseTime(hTimes[i].GetString()!);
            if (time.AddHours(1) <= now) continue; // keep the current hour
            hourly.Add(new WeatherHour(time, Num(h, "temperature_2m", i), (int)Num(h, "weather_code", i),
                Num(h, "is_day", i, 1) >= 0.5, Num(h, "precipitation_probability", i)));
        }

        var daily = new List<WeatherDay>(7);
        var d = root.GetProperty("daily");
        var dTimes = d.GetProperty("time");
        for (var i = 0; i < dTimes.GetArrayLength(); i++)
        {
            var date = DateOnly.ParseExact(dTimes[i].GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            daily.Add(new WeatherDay(date, Num(d, "temperature_2m_min", i), Num(d, "temperature_2m_max", i),
                (int)Num(d, "weather_code", i), Num(d, "precipitation_probability_max", i),
                StrAt(d, "sunrise", i) is { } sr ? ParseTime(sr) : null,
                StrAt(d, "sunset", i) is { } ss ? ParseTime(ss) : null));
        }

        var localToday = DateOnly.FromDateTime(now.ToOffset(offset).DateTime);
        var today = daily.FirstOrDefault(x => x.Date == localToday) ?? daily.FirstOrDefault();

        return new WeatherSnapshot(
            now,
            locationName,
            Num(current, "temperature_2m"),
            Num(current, "apparent_temperature"),
            (int)Num(current, "weather_code"),
            Num(current, "is_day", fallback: 1) >= 0.5,
            Num(current, "relative_humidity_2m"),
            Num(current, "wind_speed_10m"),
            today?.MinC ?? Num(current, "temperature_2m"),
            today?.MaxC ?? Num(current, "temperature_2m"),
            hourly,
            daily);
    }

    // ───────────────────────────── helpers ─────────────────────────────

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken ct)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
    }

    private static double Num(JsonElement obj, string name, double fallback = 0) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : fallback;

    private static double Num(JsonElement obj, string name, int index, double fallback = 0) =>
        obj.TryGetProperty(name, out var arr) && arr.ValueKind == JsonValueKind.Array && index < arr.GetArrayLength()
            && arr[index].ValueKind == JsonValueKind.Number ? arr[index].GetDouble() : fallback;

    private static string? StrAt(JsonElement obj, string name, int index) =>
        obj.TryGetProperty(name, out var arr) && arr.ValueKind == JsonValueKind.Array && index < arr.GetArrayLength()
            && arr[index].ValueKind == JsonValueKind.String ? arr[index].GetString() : null;

    private static string? GetString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString()!.Trim() : null;

    /// <summary>geojs returns coordinates as strings; accept numbers too.</summary>
    private static double ParseNumber(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var v)) return double.NaN;
        if (v.ValueKind == JsonValueKind.Number) return v.GetDouble();
        return v.ValueKind == JsonValueKind.String
            && double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : double.NaN;
    }

    private void SetLoading(bool loading)
    {
        lock (_gate) _isLoading = loading;
        RaiseChanged();
    }

    private void RaiseChanged() => SafeEvents.Raise(Changed, this, "WeatherService.Changed");

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        _settings.Changed -= OnSettingsChanged;
        _cts.Cancel();
    }
}
