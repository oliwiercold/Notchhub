# NotchHub — roadmap / backlog

Owner feedback is collected here so every work session (and the daily build job) knows what is next.
Status: ✅ done · 🔧 in progress · ⏳ planned · 🕓 at the very end (after the testing phase)

## Done (2026-10-13/14)
- ✅ Motion engine Legacy / Refreshed / Smooth, meeting bar, peek glance+media, phone calls/OTP/charging/swipe, Wi-Fi password connect,
  calendar day list, timer melody, mods (examples, templates, docs), Spotify like/shuffle (Web API, max 5 users - Spotify policy),
  GitHub Pages website (https://oliwiercold.github.io/Notchhub/) + recording guide, notch-style installer + release script,
  About + Buy Me a Coffee card, Apache-2.0, 26 review fixes, idle RAM reduction (tab release + aggressive trim)

## Open
- ⏳ Google one-click sign-in: owner creates the Google Cloud project (docs/GOOGLE-SETUP.md) and sends client id + secret → fill
  src/NotchHub.Core/BuiltInClients.cs; then verification (Search Console file in a `oliwiercold.github.io` repo, demo video, form)
- ⏳ First GitHub release (v1.0.0) once the owner says the build is ready - the website download button uses the latest release
- ⏳ IPhoneService actions should return Task<bool> (UI shows real success/failure for ring/ping/reply/send clipboard)
- ⏳ Power / Settings view models subscribe to app-lifetime events in constructors → move to OnShown/OnHidden so they can be released too
- ⏳ PC clipboard changes → tell PhoneService (closes the stale phone-clipboard gap fully)
- ⏳ docs/MODDING.md: document the restricted widget XAML (no ObjectDataProvider / event handlers / foreign types)
- ⏳ Pomodoro example mod: harness check "running" fails (timing/format)

## At the very end (after the testing phase)
- 🕓 "Buy me a coffee" button at the bottom of Settings with the text: "Podoba Ci się ten projekt i chciałbyś wesprzeć jego rozwój? Wesprzyj go na
  Buy Me a Coffee!" → https://www.buymeacoffee.com/gimpostin (yellow #FFDD00 style)
- 🕓 Installer — ask the owner to choose its style first
- 🕓 Choose a license, remove docs/reference (screenshots of another app) before publishing
