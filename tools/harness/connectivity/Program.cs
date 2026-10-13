// READ-ONLY diagnostics for NotchHub.Platform.Connectivity. Never calls any Set*/Connect*/Disconnect* method.
using NotchHub.Platform.Connectivity.Bluetooth;
using NotchHub.Platform.Connectivity.Display;
using NotchHub.Platform.Connectivity.Network;

using var net = new NetworkService();
await net.StartAsync();
Console.WriteLine($"[network] {net.Status}");
var sw = System.Diagnostics.Stopwatch.StartNew();
var nets = await net.ScanWifiAsync();
Console.WriteLine($"[wifi] {nets.Count} networks in {sw.ElapsedMilliseconds} ms");
foreach (var n in nets.Take(5)) Console.WriteLine($"   {n.Ssid} {n.SignalPercent}% secured={n.IsSecured} connected={n.IsConnected} profile={n.HasProfile} {n.Band}");

using var bt = new BluetoothService();
await bt.StartAsync();
await Task.Delay(2500);
Console.WriteLine($"[bluetooth] available={bt.IsAvailable} on={bt.IsEnabled} canToggle={bt.CanToggle}");
foreach (var d in bt.Devices) Console.WriteLine($"   {d.Name} {d.Kind} connected={d.IsConnected} battery={d.BatteryPercent}");

using var display = new DisplayService();
await display.StartAsync();
Console.WriteLine($"[display] nightLightSupported={display.NightLightSupported} nightLight={display.NightLightEnabled} dark={display.DarkModeEnabled} brightnessSupported={display.BrightnessSupported} brightness={display.Brightness}");
