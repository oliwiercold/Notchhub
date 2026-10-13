using System.ComponentModel;
using System.Diagnostics;
using System.Dynamic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using NotchHub.Core;
using NotchHub.Core.Settings;

namespace NotchHub.UI.Modding;

/// <summary>
/// DataContext of a widget view: bind to <c>Data.&lt;json path&gt;</c> (e.g. {Binding Data.rates[0].mid}), <c>IsLoading</c>,
/// <c>Error</c> and <c>UpdatedAt</c>. Also offers <c>Refresh</c> (an ICommand) for a refresh button.
/// </summary>
public sealed partial class WidgetDataContext : ObservableObject
{
    [ObservableProperty] private object? _data;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _error;
    [ObservableProperty] private DateTime? _updatedAt;
    public required CommunityToolkit.Mvvm.Input.IRelayCommand Refresh { get; init; }
}

/// <summary>A no-code mod: XAML view + JSON data polled from a URL or produced by a local command.</summary>
internal sealed partial class WidgetMod : IDisposable
{
    private static readonly HttpClient Http = CreateHttp();
    private readonly ModManifest _manifest;
    private readonly string _dir;
    private readonly ISettingsService _settings;
    private readonly DispatcherTimer _timer;
    private readonly WidgetDataContext _context;
    private readonly ModTile? _tile;
    private int _visibleViews;
    private bool _busy;

    public WidgetMod(ModManifest manifest, string dir, IModContext ctx, ISettingsService settings)
    {
        _manifest = manifest;
        _dir = dir;
        _settings = settings;
        _context = new WidgetDataContext { Refresh = new CommunityToolkit.Mvvm.Input.RelayCommand(() => RefreshAsync().Forget("widget refresh")) };
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(15, manifest.Data?.IntervalSeconds ?? 300)) };
        _timer.Tick += (_, _) => RefreshAsync().Forget("widget poll");

        if (manifest.Tab is { } tab)
        {
            ctx.AddTab(new ModTab
            {
                Id = manifest.Id,
                Title = string.IsNullOrWhiteSpace(tab.Title) ? manifest.Name : tab.Title,
                Icon = tab.Icon,
                CreateView = CreateView,
            });
        }
        if (manifest.Tile is { } t)
        {
            _tile = new ModTile
            {
                Id = manifest.Id + ".tile",
                Title = t.Title,
                Subtitle = t.Subtitle,
                Icon = t.Icon,
                OnClick = manifest.Tab is null ? () => RefreshAsync().Forget("widget tile") : () => ctx.OpenTab(manifest.Id),
            };
            ctx.AddTile(_tile);
            // tiles show live values, so keep polling in the background (at the manifest interval)
            _timer.Start();
            RefreshAsync().Forget("widget first load");
        }
    }

    private FrameworkElement CreateView()
    {
        var path = Path.Combine(_dir, _manifest.View);
        FrameworkElement content;
        try
        {
            using var fs = File.OpenRead(path);
            var pc = new ParserContext { BaseUri = new Uri(Path.GetFullPath(path)) };
            pc.XmlnsDictionary.Add("", "http://schemas.microsoft.com/winfx/2006/xaml/presentation");
            pc.XmlnsDictionary.Add("x", "http://schemas.microsoft.com/winfx/2006/xaml");
            content = (FrameworkElement)XamlReader.Load(fs, pc);
        }
        catch (Exception ex)
        {
            Log.Warn($"Mods: {_manifest.Id}: view failed", ex);
            content = new TextBlock { Text = $"{_manifest.Name}: {ex.Message}", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16) };
            content.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Error");
        }
        content.DataContext = _context;
        return new WidgetHost(this) { Content = content };
    }

    internal void ViewShown()
    {
        _visibleViews++;
        _timer.Start();
        if (_context.UpdatedAt is null || DateTime.Now - _context.UpdatedAt > _timer.Interval) RefreshAsync().Forget("widget shown");
    }

    internal void ViewHidden()
    {
        _visibleViews = Math.Max(0, _visibleViews - 1);
        if (_visibleViews == 0 && _tile is null) _timer.Stop();
    }

    private async Task RefreshAsync()
    {
        var data = _manifest.Data;
        if (data is null || _busy) return;
        _busy = true;
        _context.IsLoading = true;
        try
        {
            string json;
            if (!string.IsNullOrWhiteSpace(data.Url)) json = await FetchAsync(data);
            else if (!string.IsNullOrWhiteSpace(data.Command))
            {
                if (!_settings.Current.Mods.AllowCommands) throw new InvalidOperationException("commands disabled in Settings → Mods");
                json = await RunCommandAsync(data);
            }
            else return;

            var node = JsonNode.Parse(json);
            _context.Data = ToDynamic(node);
            _context.Error = null;
            _context.UpdatedAt = DateTime.Now;
            if (_tile is not null && _manifest.Tile is { } t)
            {
                _tile.Title = Expand(t.Title, node);
                _tile.Subtitle = t.Subtitle is null ? null : Expand(t.Subtitle, node);
            }
        }
        catch (Exception ex)
        {
            _context.Error = ex.Message;
            Log.Warn($"Mods: {_manifest.Id}: data refresh failed: {ex.Message}");
        }
        finally
        {
            _context.IsLoading = false;
            _busy = false;
        }
    }

    private static async Task<string> FetchAsync(ManifestData data)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, data.Url);
        foreach (var (k, v) in data.Headers) req.Headers.TryAddWithoutValidation(k, v);
        using var resp = await Http.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync();
    }

    private async Task<string> RunCommandAsync(ManifestData data)
    {
        var psi = new ProcessStartInfo(data.Command!)
        {
            WorkingDirectory = _dir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
        };
        foreach (var a in data.Args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("could not start " + data.Command);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var output = await p.StandardOutput.ReadToEndAsync(cts.Token);
        await p.WaitForExitAsync(cts.Token);
        return output;
    }

    /// <summary>Replaces {a.b.0.c} placeholders with values from the JSON.</summary>
    internal static string Expand(string template, JsonNode? root) =>
        PlaceholderRegex().Replace(template, m =>
        {
            JsonNode? n = root;
            foreach (var part in m.Groups[1].Value.Split('.'))
            {
                n = n switch
                {
                    JsonObject o => o[part],
                    JsonArray a when int.TryParse(part, out var i) && i >= 0 && i < a.Count => a[i],
                    _ => null,
                };
                if (n is null) break;
            }
            return n switch
            {
                JsonValue v when v.TryGetValue<double>(out var d) => d.ToString("0.####", CultureInfo.CurrentCulture),
                JsonValue v => v.ToString(),
                null => "",
                _ => n.ToJsonString(),
            };
        });

    /// <summary>JSON → ExpandoObject / List / primitives (WPF binds to ExpandoObject members by name).</summary>
    internal static object? ToDynamic(JsonNode? node) => node switch
    {
        JsonObject o => o.Aggregate((IDictionary<string, object?>)new ExpandoObject(), (d, kv) => { d[kv.Key] = ToDynamic(kv.Value); return d; }),
        JsonArray a => a.Select(ToDynamic).ToList(),
        JsonValue v when v.TryGetValue<bool>(out var b) => b,
        JsonValue v when v.TryGetValue<double>(out var d) => d,
        JsonValue v when v.TryGetValue<string>(out var s) => s,
        JsonValue v => v.ToString(),
        _ => null,
    };

    [GeneratedRegex(@"\{([A-Za-z0-9_.\-]+)\}")]
    private static partial Regex PlaceholderRegex();

    private static HttpClient CreateHttp()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("NotchHub/1.0 (mods)");
        return c;
    }

    public void Dispose() => _timer.Stop();

    /// <summary>Hosts a widget view and forwards visibility to the data poller.</summary>
    private sealed class WidgetHost(WidgetMod owner) : ContentControl, ITabView
    {
        public void OnShown() => owner.ViewShown();
        public void OnHidden() => owner.ViewHidden();
    }
}
