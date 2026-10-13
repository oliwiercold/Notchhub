# NotchHub — roadmap / backlog

Owner feedback is collected here so every work session (and the daily build job) knows what is next.
Status: ✅ done · 🔧 in progress · ⏳ planned · 🕓 at the very end (after the testing phase)

## In progress (wave 6, 2026-10-13)
- 🔧 Motion engine: Legacy / Refreshed / Smooth (motion blur, high refresh), interruptible physics springs (docs/MOTION.md)
- 🔧 Tab indicator overlap (right side too wide), Power tab bottom gap, reply text field caret/text offset
- 🔧 Meeting bar in the idle notch (countdown to start / end, all calendars, settings), notch widens for long titles
- 🔧 Peek: glance card next to the mini player (setting, default on)
- 🔧 Reserve screen space: third mode "Dynamic" (follows what the idle notch shows)
- 🔧 Phone: call status (telephony), mute ringer, charging HUD, OTP one-click copy, swipe-to-dismiss notifications, battery-unknown state
- 🔧 Wi-Fi: connect to saved networks, password prompt for new ones
- 🔧 Today: click a day → list of all its events
- 🔧 Timer: long, soft melody instead of a short beep
- 🔧 Mods: examples (NBP rates, script widget, Pomodoro), templates, docs/MODDING.md, settings section, tiles row
- 🔧 Media card: shuffle / repeat buttons

## Planned
- ⏳ Spotify: "add to library" (like) and Smart Shuffle — needs the Spotify Web API (user's own client id, PKCE); Smart Shuffle is not exposed by
  Spotify's public API (investigate), plain shuffle works through Windows media controls
- ⏳ GitHub Pages website (`website/`): black-orange, cozy, Google fonts, smooth transitions, video slots, download button that optionally asks for
  Buy Me a Coffee first, FAQ below the download section
- ⏳ Recording guide for the website videos (OpenScreen: what to show, click-by-click)
- ⏳ Daily test build + changelog at 21:00 (scheduled task `notchhub-daily-build`)

## Final stage (started 2026-10-13 evening)
- 🔧 Notch-style installer (owner's choice) with the most important settings as options + uninstaller, release script, GitHub release workflow
- 🔧 "O aplikacji" + Buy Me a Coffee card at the bottom of Settings
- 🔧 Multi-area bug review + fixes
- ✅ License: Apache-2.0 (LICENSE, NOTICE); reference screenshots of the other app are no longer tracked by git

## At the very end (after the testing phase)
- 🕓 "Buy me a coffee" button at the bottom of Settings with the text: "Podoba Ci się ten projekt i chciałbyś wesprzeć jego rozwój? Wesprzyj go na
  Buy Me a Coffee!" → https://www.buymeacoffee.com/gimpostin (yellow #FFDD00 style)
- 🕓 Installer — ask the owner to choose its style first
- 🕓 Choose a license, remove docs/reference (screenshots of another app) before publishing
