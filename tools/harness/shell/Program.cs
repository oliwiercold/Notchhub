using System.IO;
// Read-only diagnostics for NotchHub.Platform.Shell. Never writes the clipboard, sends keys or changes autostart.
using NotchHub.Core;
using NotchHub.Core.Contracts;
using NotchHub.Core.Settings;
using NotchHub.Platform.Shell.Autostart;
using NotchHub.Platform.Shell.Clipboard;
using NotchHub.Platform.Shell.Foreground;
using NotchHub.Platform.Shell.Hotkeys;
using NotchHub.Platform.Shell.Wallpaper;

var root = Path.Combine(Path.GetTempPath(), "NotchHubHarnessShell");
AppPaths.Initialize(root);
var fails = 0;
void Check(bool ok, string what) { if (!ok) fails++; Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {what}"); }

(string, ClipKind)[] cases =
[
    ("https://example.com/a?b=1", ClipKind.Link), ("www.google.com", ClipKind.Link), ("#ff8800", ClipKind.Color),
    ("rgb(10, 20, 30)", ClipKind.Color), ("hello world", ClipKind.Text), ("decade", ClipKind.Text),
    ("public class Foo\n{\n    int x = 1;\n}", ClipKind.Code), ("var x = Foo();", ClipKind.Code),
    ("To jest zwykłe zdanie, nic więcej.\nDruga linia tekstu.", ClipKind.Text), ("mailto:a@b.pl", ClipKind.Link),
];
foreach (var (text, kind) in cases) Check(TextClassifier.Classify(text) == kind, $"classify '{text.Replace("\n", "⏎")}' → {TextClassifier.Classify(text)}");

string[] good = ["Ctrl+Alt+Space", "Win+Shift+N", "Ctrl+F12", "F13", "Ctrl+Alt+`", "Ctrl++", "Alt+PgDn", "Ctrl+Shift+/"];
string[] bad = ["", "Ctrl+", "Space", "Ctrl+Ctrl+A", "Ctrl+Foo", "A+Ctrl"];
foreach (var g in good) Check(GestureParser.TryParse(g, out _, out _), $"gesture valid '{g}'");
foreach (var g in bad) Check(!GestureParser.TryParse(g, out _, out _), $"gesture invalid '{g}'");

Console.WriteLine($"[autostart] enabled={new AutostartService().IsEnabled}");

using var wallpaper = new WallpaperService();
await wallpaper.StartAsync();
Console.WriteLine($"[wallpaper] path={(wallpaper.CurrentPath is null ? "-" : Path.GetFileName(wallpaper.CurrentPath))} solid={wallpaper.SolidColor:X8}");
var sw = System.Diagnostics.Stopwatch.StartNew();
var px = await wallpaper.GetPixelsAsync(64);
Check(px is not null && Math.Max(px.Width, px.Height) <= 64, $"wallpaper pixels {px?.Width}x{px?.Height} in {sw.ElapsedMilliseconds} ms, first=0x{px?.Argb[0]:X8}");

using var fg = new ForegroundService();
await fg.StartAsync();
Console.WriteLine($"[foreground] fullscreen={fg.IsFullscreenAppActive} lastExternal={fg.LastExternalAppName}");

using var hotkeys = new HotkeyService();
await hotkeys.StartAsync();
Check(hotkeys.Register("harness", "Ctrl+Alt+Shift+F24", () => { }), "hotkey register Ctrl+Alt+Shift+F24");
Check(hotkeys.Register("harness", "Ctrl+Alt+Shift+F23", () => { }), "hotkey replace by id");
hotkeys.Unregister("harness");

var settings = new SettingsService(Path.Combine(root, "settings.json"));
using (var clip = new ClipboardService(settings))
{
    await clip.StartAsync();
    Check(clip.IsMonitoring, "clipboard monitoring");
    var text = await clip.GetSystemClipboardTextAsync();
    Console.WriteLine($"[clipboard] system text length={text?.Length ?? -1}, history={clip.Items.Count}");
    clip.AddExternal("https://example.com/harness", ClipOrigin.Phone, setSystemClipboard: false);
    clip.AddExternal("https://example.com/harness", ClipOrigin.Phone, setSystemClipboard: false);
    Check(clip.Items.Count(i => i.Text == "https://example.com/harness") == 1 && clip.Items[0].Kind == ClipKind.Link, "dedupe + link kind");
    var id = clip.Items[0].Id;
    clip.SetPinned(id, true);
    clip.Clear();
    Check(clip.Items.Any(i => i.Id == id), "clear keeps pinned");
    clip.Delete(id);
}
Check(File.Exists(Path.Combine(root, "clipboard", "history.json")), "history.json written");
Console.WriteLine(fails == 0 ? "ALL OK" : $"{fails} FAILED");
