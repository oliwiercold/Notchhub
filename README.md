# NotchHub

A "dynamic notch" for Windows 11: a tiny black notch at the top of the screen that morphs into a mini player, heads-up
notifications and a full control panel — styled like **Android 16 / Material 3 Expressive**, with colors taken from your wallpaper.

Native C# / .NET 10 / WPF. No Electron, no background browser.

## Features

| Tab | What it does |
|---|---|
| **Controls** | Wi-Fi / Ethernet, Bluetooth, audio output (device switcher + per-app mixer), Night light, Dark mode, brightness (laptop panels and DDC/CI monitors), volume, media card for any app that uses Windows media controls (Spotify, browsers, …) |
| **Today** | Big clock, weather (Open-Meteo, no API key), week strip with events, timer, to-do list |
| **Stats** | CPU / GPU / RAM gauges, disk, network, uptime, top apps by RAM or CPU, internet speed test (Cloudflare) |
| **Clipboard** | Searchable history (text, links, code, colors, images, files), pinning, multi-select, paste into the previous app, skips password-manager content |
| **Phone** | Built-in **KDE Connect** endpoint: phone battery & signal, notifications (read, reply, dismiss, actions), ring, ping, clipboard sync |
| **Power** | Lock, sleep, restart, shut down (restart / shut down ask twice) |

The notch also shows a live activity (album art + equalizer) while music plays, a mini player on hover, HUDs for volume
changes / phone notifications / timers, hides itself when a fullscreen app or game is in front, and can optionally
**reserve screen space** (Windows AppBar) so maximized windows end right below it.

Calendar sources: local events, any ICS link (including the secret iCal address of a Google calendar) and a full
**Google Calendar** connection (OAuth, read & write).

## Requirements

* Windows 10 2004+ / Windows 11, x64
* [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (included in the .NET 10 SDK)

## Build & run

```bash
dotnet build NotchHub.slnx -c Release
```

```bash
src/NotchHub.App/bin/Release/net10.0-windows10.0.22621.0/NotchHub.exe
```

Single-file build:

```bash
dotnet publish src/NotchHub.App/NotchHub.App.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

Useful flags: `--demo` (sample data, nothing touches your system), `--data <dir>` (separate settings folder),
`--screenshot <dir>` (renders every state to PNG, use with `--demo`).

Global shortcuts (changeable in Settings → Shortcuts): **Ctrl+Alt+Space** toggles the panel, **Ctrl+Alt+V** opens the clipboard.

## Pairing a phone (KDE Connect)

1. Install **KDE Connect** on Android (Google Play / F-Droid).
2. Phone and PC on the same Wi-Fi network; allow NotchHub in the Windows Firewall prompt (private networks).
3. In KDE Connect on the phone your PC appears under its name → *Request pairing*, then accept in the notch.
4. On the phone enable the *Notification sync* plugin for this PC and grant notification access.

If the official KDE Connect desktop app also runs on the PC, both appear as separate devices — that is fine.

## Google Calendar

*Quick, read-only:* Google Calendar → Settings → your calendar → *Integrate calendar* → copy the **secret address in iCal
format** → NotchHub Settings → Calendar → add it as a subscription.

*Full (read & write):* NotchHub is open source, so it cannot ship its own Google keys. Create a free OAuth client once
(≈5 minutes):

1. [Google Cloud Console](https://console.cloud.google.com/) → create a project.
2. *APIs & Services → Library* → enable **Google Calendar API**.
3. *OAuth consent screen* → External → fill in the app name → add your Gmail address as a **test user**.
4. *Credentials → Create credentials → OAuth client ID* → application type **Desktop app**.
5. Copy the client ID and client secret into NotchHub Settings → Calendar → **Connect with Google**.

Tokens are stored locally, encrypted with Windows DPAPI.

## Privacy

Everything stays on your PC (`%APPDATA%\NotchHub`). Network access: Open-Meteo (weather), get.geojs.io (approximate
location when no city is set), Cloudflare (only when you run a speed test), Google (only if you connect a calendar),
your phone over the local network (KDE Connect).

## Project layout

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) (projects, conventions), [docs/DESIGN.md](docs/DESIGN.md) (design spec) and
[docs/UI-COMPONENTS.md](docs/UI-COMPONENTS.md) (Material 3 Expressive controls).

## Credits

* Fonts: [Google Sans Flex](https://fonts.google.com/specimen/Google+Sans+Flex) (SIL OFL 1.1, bundled as renamed static
  instances "NH Sans"), [Material Symbols](https://github.com/google/material-design-icons) (Apache 2.0) — see
  [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
* KDE Connect protocol by the [KDE Connect](https://kdeconnect.kde.org/) project (NotchHub is an independent implementation).

## License

[Apache License 2.0](LICENSE) — see also [NOTICE](NOTICE) and [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
