# NotchHub — feature tab specs (one section per feature project)

## controls (NotchHub.Features.Controls)
Reference docs/reference/1.png. ControlsView + ControlsViewModel (+ sub view models / user controls as you like).
* Row 1 (3 equal QsTiles with chevrons): Internet (Wi-Fi: SSID / "Ethernet" / "Brak połączenia"; icon by kind + signal bars; tap toggles the Wi-Fi radio
  only when NetworkStatus.CanToggleWifi, otherwise opens the networks sheet; chevron → networks sheet: scan with LoadingIndicator, list (signal icon,
  lock, "Połączono"), click a network with saved profile → ConnectWifiAsync, else open settings; footer button "Ustawienia sieci"),
  Bluetooth (On/Off/connected device name; tap toggles radio; chevron → paired devices sheet with connect/disconnect + battery), Audio output
  (default device name + kind icon; tap toggles mute; chevron → sheet with output devices (radio list → SetDefaultOutputAsync) and per-app mixer
  (M3Slider per AudioAppSession with app icon)).
* Row 2: Night light (QsTile + chevron → OpenNightLightSettings; hidden/disabled state when unsupported), Dark mode (QsTile toggles).
* Brightness M3Slider row (icon brightness_6) only when Display.BrightnessSupported. Volume M3Slider (icon volume_up/down/mute/off by level; label "64%").
  Slider drags → SetVolume immediately (service throttles); never fight the user: while dragging ignore incoming VolumeChanged.
* Media card (Card): top line source = audio device icon + name ("Głośniki") + app name; art 56 px rounded 14 (placeholder music_note when null),
  title (TitleL, marquee or ellipsis), artist; controls: skip_previous IconButton, PlayPauseButton (morph), skip_next; WavyProgressBar seekable
  (IsWavy = playing) with live position (interpolate locally at 30 fps only while visible + playing) and times "0:57" / "62:15"; when several
  sessions exist, small chips to switch session; empty state "Nic nie gra" with a subtle icon.
* Everything reacts live to service Changed events (marshal with AppServices.Ui.Post).

## today (NotchHub.Features.Today)
Reference docs/reference/5.png. Layout: left column (clock card + weather card), middle (week card with next event; timer card), right (to-do card).
* Clock: hours / three dots (accent) / minutes stacked in Text.Display (NH Sans Rounded), 24 h for Polish (12 h + AM/PM for English),
  date line "wt. • 13 paź" (localized), updates every second only while visible (align to minute changes).
* Weather: icon (Material Symbols mapped from WMO code + IsDay, tinted Warning for sun), temperature in settings unit, localized description
  (today.weather.<code>), min/max, location; loading / error / disabled states; click → Weather.RefreshAsync.
* Week strip (Mon–Sun per settings WeekStartsMonday): day names + numbers, today = PrimarySoft pill, days with events get a small dot, click a day
  selects it (the event card then shows that day's events); header "Ten tydzień" + date range.
* Next event card (Pink vertical bar = event color): "Następne • 11:00" + title (+ "teraz" when ongoing, "jutro"/weekday when not today); if none:
  "Brak wydarzeń". "+" IconButton opens a SheetHost to add a local event (title TextField, date chips (today/tomorrow/date picker via +/- day
  buttons), start time with hour/minute steppers, duration chips 15/30/60/90 min, color swatches) → Calendar.AddLocalEvent. Right-click/long
  press on an event → delete local event.
* Timer: WavyRing showing remaining fraction (IsWavy while running), center text m:ss (h:mm:ss over an hour); h/min fields (mouse wheel and
  arrows adjust, typing allowed); preset chips 1m 5m 10m 15m 20m (FilterChip, selected = current duration); Start (M3Button Filled) /
  Pause / Resume, Reset (Tonal); +1 min while running.
* To-do: header "Dzisiaj" + small progress ring "1/3"; list with Checkbox + text (strike-through + OnSurfaceMuted when done, animated);
  hover shows delete IconButton; double-click edits inline; add row: TextField "Dodaj zadanie…" + arrow IconButton (Enter adds);
  empty state.

## stats (NotchHub.Features.Stats)
Reference docs/reference/2.png. Take a live-updates token in OnShown (AppServices.Stats.RequestLiveUpdates()) and dispose it in OnHidden.
* Three gauge cards (CPU / GPU / RAM): WavyRing (Gauge.Cpu/Gpu/Ram brushes, value animates), center "37%" (Text.Number), name line (Title),
  sub line (CPU model / GPU name / "19.4 / 31.9 GB" — BodyS, ellipsis), chips row ("4.2 GHz" + "229 proc." / VRAM "2.1 / 10.0 GB" + temp if known /
  "12.5 GB wolne"). GPU card shows "—" state when Gpu is null.
* Bottom row cards: Disk (drive from settings, "412 / 931 GB" + thin linear bar), Network (↓ 1.2 MB/s ↑ 300 KB/s with arrow icons, adaptive units),
  Uptime ("5 godz. 12 min" localized).
* Right column: "Najpopularniejsze aplikacje • RAM" card with a RAM⇄CPU toggle (persist settings.Stats.TopAppsSort), rows: icon (IconPng via
  BytesToImage, else Avatar letter), name, thin bar relative to the top item (animated), value ("1.4 GB" / "12%"). Top 5-6 rows.
* Internet speed card: "Szybkość internetu" + "ostatnio: 5 min temu • Warszawa"; big numbers ↓/↑ Mbps + ping; "Uruchom test" M3Button;
  while running: phase label, live Mbps in big text, LoadingIndicator / progress, Cancel; failed state with retry.
* Number formatting with Loc.Culture; values update smoothly (no flicker / layout jumps: fixed widths for numbers, tabular figures if possible).

## clipboard (NotchHub.Features.Clipboard)
Reference docs/reference/6.png. Two-pane layout: left list (≈ 55 %), right preview card.
* Top bar: TextField with search icon "Szukaj w 23 elementach…" (live filter, case/diacritics-insensitive), filter chips Wszystko / Tekst /
  Obrazy / Linki (+ Kod, Kolory if you like), settings IconButton → Shell.Expand(Settings).
* List grouped PRZYPIĘTE / DZISIAJ / WCZORAJ / STARSZE (Text.Overline headers), rows (ListRow): kind icon (subject/link/code/image/palette/
  description), single-line preview (images: small thumbnail + "Obraz • 1920×1080", colors: swatch + hex, files: name + count), relative time,
  phone badge for Origin=Phone; hover reveals pin + delete IconButtons; selected row = SecondaryContainer; multi-select with Ctrl/Shift click.
* Preview pane: text (wrapping, scrollable, selectable), image (fit, rounded), link (domain + full URL + "Otwórz" button), color (big swatch +
  hex/rgb), files (list); metadata rows: Typ, Rozmiar ("108 znaków • 22 słowa • 1 linia" / "1920×1080" / "3 pliki"), Skopiowano (localized
  time), Źródło (SourceApp / "Telefon").
* Keyboard (when the tab is active and the search box is not consuming the key): Enter = paste (Clipboard.PasteAsync of selected ids),
  Ctrl+C copy, Ctrl+P pin/unpin, Del remove, ↑/↓ move selection (Shift extends), Tab cycles filters, typing focuses search. Double-click = paste.
  Footer key hints like the reference (small keycaps: Surface3 rounded 6, Text.LabelS).
* Empty states (no items / no results), "Wyczyść historię" (keeps pinned) with an inline confirm. Virtualize the list (VirtualizingStackPanel)
  for 300+ items; thumbnails via a cached image loader (decode at small size off the UI thread).

## phone (NotchHub.Features.Phone)
New tab (no reference) in the same visual language. Data: AppServices.Phone.
* Paired + reachable: left device card — smartphone icon in a Surface3 circle, device name (Title), status line "Połączono • Wi-Fi" /
  "Offline", big battery WavyRing (Success color; Warning ≤ 20 %; Error when IsLow) with "76%" + bolt icon when charging, signal bars + network
  type chip ("5G"), action buttons (Tonal M3Buttons / IconButtons): "Zadzwoń" (RingAsync – find phone), "Ping", "Wyślij schowek" (current PC
  clipboard text → SendClipboardAsync), refresh.
* Right: notifications card: header "Powiadomienia • 5" + "Wyczyść wszystko" (dismiss all clearable); list newest first; each row: app icon
  (IconPng or Avatar), app name + relative time, title (Title), text (one line, ellipsis); click expands (accordion, animated height): full text,
  action chips (InvokeNotificationActionAsync), reply TextField + send (if ReplyId), dismiss IconButton (if clearable). Animate rows
  added/removed (fade + height). Empty state "Brak powiadomień".
* Pairing flows: phone feature disabled (Settings.Phone.Enabled false or !IsRunning) → explanation + button to settings; no devices → onboarding
  card: steps (install KDE Connect from Google Play/F-Droid, same Wi-Fi network, allow NotchHub in the Windows Firewall prompt, open KDE
  Connect → this PC "<LocalDeviceName>" appears → Pair) + Refresh button (RefreshAsync) + LoadingIndicator "Szukam urządzeń…"; discovered
  unpaired devices list with "Sparuj" (RequestPairAsync); RequestedByUs → waiting state with VerificationKey + cancel; RequestedByPeer →
  Accept/Reject with VerificationKey; paired but offline → greyed device card "Offline" + last notifications cleared message + Unpair in
  a small overflow.

## power+compact (NotchHub.Features.Power and NotchHub.Features.Compact)
Power — reference docs/reference/3.png: four big tiles in one row (Zablokuj lock, Uśpij bedtime, Uruchom ponownie restart_alt, Wyłącz
power_settings_new): Surface2 cards radius 24, icon 28 + label; hover state layer, press squish; Restart/Shut down need confirmation: first
click morphs the tile to Error/ErrorContainer colors with a 4 s countdown ring and "Kliknij ponownie", second click executes
(AppServices.Power.ExecuteAsync), timeout reverts; Lock/Sleep execute immediately (after Shell.Collapse()). Hide unsupported actions.
Compact (these views are hosted by the shell inside the notch; keep them tight and pixel-perfect, they are what the user sees most):
* LiveActivityView (collapsed notch with media playing, ≈300×34): album art 22 px radius 6 on the left, animated 4-bar equalizer (Primary,
  bars bounce with randomized spring heights while playing, flatten when paused) on the right; HasActivity = a session is playing
  (keep true for ~5 s after pause to avoid flicker), drives the shell.
* PeekView (hover): ShowsMedia = media session exists → mini player like docs/reference/4.png (art 44, title/artist, WavyProgressBar with times,
  prev / PlayPauseButton / next), else Glance: big time (Rounded), date, weather icon + temp, next event line, phone battery + notification
  count if paired.
* HudView.Present(HudRequest): Volume/Brightness → icon + level bar (M3Slider-like, interactive: dragging changes volume/brightness) + %;
  PhoneNotification → app icon, app name, title, text (2 lines); click → AppServices.Shell.Expand(ShellTab.Phone); PhonePairRequest → device +
  verification key + Accept/Reject buttons (Phone.AcceptPairAsync/RejectPairAsync); TimerFinished → timer icon (animated), "Czas minął!",
  buttons "+1 min" (Timer.Start(1 min)) and "Stop"; Info → icon + text. Each kind has its own compact layout; transitions between kinds
  animate.

## settings (NotchHub.Features.Settings)
Opened from the gear in the panel header (top-right). Two-pane: left navigation (ListRow list with icons: Wygląd, Notch, Skróty, Schowek,
Pogoda, Kalendarz, Telefon, Ogólne), right scrollable content; every change applied live via AppServices.Settings.Update.
* Wygląd: color source as Android-style swatch cards (up to 4 wallpaper candidates from NotchHub.UI.Theming.ThemeService.Current
  .WallpaperCandidates, the wallpaper average, Windows accent, custom (hex TextField + preset swatches)); scheme variant chips (Tonal Spot,
  Vibrant, Expressive, Fidelity, Content, Neutral, Monochrome, Rainbow, Fruit salad) each with a 3-color mini preview from
  ThemeService.Generate; panel style (Czarny / Barwiony); contrast slider; reduce motion switch.
* Notch: monitor picker, collapsed width/height sliders, hover behavior (segmented chips), hover / collapse delay sliders, live activity, HUDs,
  scroll-to-expand, hide in fullscreen, default tab, remember last tab, **Rezerwuj miejsce na ekranie** switch (+ "Wysokość: Notch /
  Z aktywnością" chips) with a one-line explanation ("zmaksymalizowane okna kończą się pod notchem").
* Skróty: hotkey capture fields (click → "Naciśnij skrót…", records the next key combo, Esc cancels, Backspace clears; validate with
  AppServices.Hotkeys.IsValidGesture) for TogglePanel and OpenClipboard.
* Schowek: enabled, max items slider, keep images, ignore sensitive, retention days, ignored apps (list + add exe name), "Wyczyść historię".
* Pogoda: enabled, city search (Weather.SearchLocationAsync → results list → sets City/Lat/Lon/LocationName), "Użyj lokalizacji z IP",
  unit °C/°F.
* Kalendarz: ICS subscriptions (name, URL, color, enabled, remove; add form), week starts Monday, refresh interval; per-source status from
  Calendar.Sources.
* Telefon: enabled switch, device name, this PC's id, status (IsRunning), paired devices with "Rozłącz parowanie" (UnpairAsync), notification
  peeks, receive clipboard, muted apps; how-to-pair hint.
* Ogólne: language (Polski / English → settings.Language; Loc switches live), start with Windows, open data folder, open logs, version/about.
