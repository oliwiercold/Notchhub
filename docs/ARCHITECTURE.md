# NotchHub — Architecture & rules for contributors (humans and agents)

Native Windows 11 app, **C# / .NET 10 / WPF**, no other runtimes. Solution: `NotchHub.slnx`.
Development machine notes: the app is developed and tested on a Windows 11 desktop that is online **through Wi-Fi** (never toggle the
radio while testing), with an NVIDIA GPU (NVML available) and the official KDE Connect desktop installed (handy for interop tests).
UI language default: Polish (switchable to English).

## Projects

| project | TFM | owns |
|---|---|---|
| `NotchHub.Core` | net10.0 | contracts (`Contracts/*.cs`), models, `AppSettings`, `SettingsService`, `AppServices` locator, `Log`, `JsonStore`, `AppPaths` |
| `NotchHub.UI` | windows + WPF | design system: tokens, typography, controls, converters, motion, `Theming/ThemeService`, `Localization/Loc`, fonts |
| `NotchHub.KdeConnect` | net10.0 | KDE Connect protocol endpoint → `PhoneService : IPhoneService` |
| `NotchHub.Platform.Media` | windows | `Media/MediaService` (SMTC), `Audio/AudioService` (Core Audio) |
| `NotchHub.Platform.Connectivity` | windows | `Network/NetworkService`, `Bluetooth/BluetoothService`, `Display/DisplayService` |
| `NotchHub.Platform.Machine` | windows | `Stats/StatsService`, `SpeedTest/SpeedTestService`, `Power/PowerService` |
| `NotchHub.Platform.Shell` | windows + WPF | `Clipboard/ClipboardService`, `Foreground/ForegroundService`, `Hotkeys/HotkeyService`, `Wallpaper/WallpaperService`, `Autostart/AutostartService` |
| `NotchHub.Services` | net10.0 | `Weather/WeatherService` (Open-Meteo), `Calendar/CalendarService` (local + ICS), `Todos/TodoService`, `Timer/TimerService` |
| `NotchHub.Demo` | windows + WPF | `DemoServices.Install()` — fake implementations of every contract with sample data (`--demo`, screenshots) |
| `NotchHub.Features.*` | windows + WPF | one per tab (Controls, Today, Stats, Clipboard, Phone, Power, Settings) + `Compact` (live activity, peek, HUD views) |
| `NotchHub.App` (exe `NotchHub.exe`) | windows + WPF | composition root, notch window/shell, tray, hotkeys wiring, screenshot mode |
| `tests/*` | | xUnit tests |

"windows" TFM = `$(WindowsTfm)` = `net10.0-windows10.0.22621.0` (WinRT APIs such as `Windows.Media.Control`,
`Windows.Devices.Radios`, `Windows.Networking.Connectivity` are directly usable).

Dependency direction: Core ← (everything). UI ← Features, App. Platform/Services/KdeConnect/Demo reference **only Core**
(+ NuGet). Features reference Core + UI only and get services from `AppServices.*` at runtime. App references everything.

## Conventions

* Implementation class names, namespaces and constructor signatures are fixed by the skeleton stubs (see
  `NotchHub.App/Composition.cs`). Replace stub bodies, keep the signatures.
* `IBackgroundService.StartAsync` must not throw for missing hardware/permissions: degrade, log, expose availability flags.
* Events may fire on any thread; view models marshal with `AppServices.Ui.Post(...)`.
* State properties return immutable snapshots (records, arrays) — safe to read from any thread; raise `Changed` after updating.
* Persistence: `JsonStore.Load/Save` with files under `AppPaths.Root` (`%APPDATA%\NotchHub`). Each service owns its files:
  `settings.json` (Core), `todos.json`, `calendar.json`, `clipboard/`, `kdeconnect/`, `speedtest.json`, `weather-cache.json`.
* Logging: `NotchHub.Core.Log.Info/Warn/Error` (never `Console.WriteLine` in library code). Never log secrets or clipboard contents.
* Localization: strings live in each assembly's `Strings/pl.xaml` + `Strings/en.xaml` (sys:String resources) with keys
  prefixed by the feature (`controls.*`, `today.*`, `stats.*`, `clipboard.*`, `phone.*`, `power.*`, `settings.*`,
  `compact.*`, `shell.*`, `common.*` in NotchHub.UI). XAML: `{DynamicResource key}`; code: `Loc.Get(key)` / `Loc.Format(key, …)`.
  Services never produce user-facing text (return codes/enums; the UI localizes). Polish text must use proper diacritics.
* No `async void` except event handlers; fire-and-forget through `task.Forget("context")` (Core `Log`).
* Nullable enabled; keep warnings at zero in your projects.
* HTTP: one static `HttpClient` per service with a `User-Agent: NotchHub/1.0` header and timeouts.

## Performance budget (the user explicitly wants a lightweight app)

* Idle (panel collapsed, nothing playing): ~0 % CPU, no timers firing more often than every few seconds, no per-frame rendering.
  Target working set ≲ 120 MB, private bytes as low as practical.
* Live updates (stats polling, clocks, media progress interpolation, equalizer/wavy animations) run ONLY while the view showing them is
  visible (ITabView.OnShown/OnHidden, IsVisible) — stop timers / CompositionTarget.Rendering handlers when hidden.
* Prefer OS event notifications over polling; when polling is unavoidable use long intervals and back off when hidden.
* Cache decoded images (frozen, decoded at display size via DecodePixelWidth); never keep full-size bitmaps for thumbnails.
* No allocations in per-frame paths; freeze brushes/geometries that do not change; avoid Effects (blur/shadow) on large animated areas.
* Lazy-create tab views on first show; release heavy data of hidden tabs when reasonable.

## Parallel work rules (IMPORTANT)

Several agents work at the same time in this repository.

1. **Only edit files inside the paths you own** (your task names them). Never edit `NotchHub.Core` — if a contract is
   wrong or missing something, work around it inside your project and list the requested change in your final report.
2. **Build only your own project(s)**, never the solution, with
   `dotnet build src/<Project>/<Project>.csproj --no-dependencies --no-restore -nologo -v q`
   (all dependencies are pre-built). If you add a NuGet package: `dotnet restore src/<Project>/<Project>.csproj` first.
   If a build fails with a file-lock / "being used by another process" error, wait a few seconds and retry.
3. Scratch / harness programs go in `tools/harness/<your-name>/` (or the session scratchpad) — never inside `src/` of a
   project you do not own. Build them with `--no-dependencies` too (build your library first).
4. **Do not run git commands that change state** (commit, checkout, reset, stash…). Read-only git is fine.
5. Safety on the user's real machine (it is in use and this session's own internet goes through its Wi-Fi):
   * NEVER turn off Wi-Fi or Bluetooth radios, disconnect networks, change the default audio device, execute power
     actions (lock/sleep/restart/shutdown/sign-out), send keystrokes (SendInput) or toggle Windows dark mode / night light
     while testing. Read-only checks only; code-review the write paths.
   * Volume / brightness / clipboard: if a test must write, restore the original value immediately afterwards.
   * Bind test network listeners to 127.0.0.1 (avoids Windows Firewall prompts).
6. When you finish, report: what you implemented, how you verified it (commands + results), known gaps, contract changes
   you need.

## Running

* Real: `src/NotchHub.App/bin/Debug/net10.0-windows10.0.22621.0/NotchHub.exe`
* Demo: `NotchHub.exe --demo --data %TEMP%\nh-demo`
* Screenshots of every state (demo data): `NotchHub.exe --demo --data %TEMP%\nh-shots --screenshot <dir>`
* Capture the real screen region (includes layered windows): `pwsh tools/capture.ps1 -Out shot.png -W 1000 -H 660`
