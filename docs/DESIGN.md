# NotchHub — Design spec (Material 3 Expressive, "Android 16" look)

NotchHub is a Windows "dynamic notch": a black notch at the top-center of the screen that morphs into a mini player /
heads-up / full control panel. Reference screenshots (from the app the user wants cloned) are in `docs/reference/`:

| file | shows |
|---|---|
| `1.png` | **Controls** tab: Wi-Fi / Bluetooth / Audio tiles (split chevron), Night Light / Dark mode tiles, volume slider 64 %, media card |
| `2.png` | **Stats** tab: CPU / GPU / RAM ring gauges, disk / network / uptime cards, "Top apps · RAM" list with CPU⇄ toggle, internet speed test card |
| `3.png` | **Power** tab: Lock / Sleep / Restart / Shut down tiles |
| `4.png`, `7.webp` | **Peek** (hover) mini player inside the notch: art, title, artist, progress, prev / pause / next |
| `5.png` | **Today** tab: big clock, weather, week strip with today highlighted, next event, timer with presets, to-do list |
| `6.png` | **Clipboard** tab: search, filter chips, grouped list (Pinned / Today / Yesterday), preview pane with metadata, key hints |
| `8.webp` | **Collapsed** notch: tiny black notch with inverse rounded "shoulders" at the top edge |

The user explicitly does **not** want the *Apps* and *Wallpaper* tabs. We add a **Phone** tab (KDE Connect) instead.
Tabs: **Controls · Today · Stats · Clipboard · Phone · Power** (+ Settings opened from a gear button).

Style direction: same layout/density as the references, but styled like **Android 16 / Material 3 Expressive**:
thick sliders with a bar handle, wavy progress indicators, shape-morphing buttons & tiles, pill tabs, dynamic color
taken from the wallpaper, generous rounding, springy motion. Dark UI only (the notch is "hardware").

---

## 1. Color (dynamic, from the wallpaper)

All colors are tokens in `NotchHub.UI/Themes/Tokens.xaml` and are **replaced at runtime** by `ThemeService`
(source color = dominant wallpaper color (Material You quantize+score), or wallpaper average, or Windows accent, or custom;
scheme variant TonalSpot / Vibrant / Expressive / Fidelity / … as on Android). Rules:

* Reference brushes **only** via `{DynamicResource Brush.X}`. Never hard-code colors (exceptions: `Transparent`).
* Custom-drawn controls read brushes through DependencyProperties whose defaults are set in styles via DynamicResource
  (so they follow theme changes), not by caching a Color at construction time.

| token | dark tone (source palette) | use |
|---|---|---|
| `Panel` | black (or neutral T4 when PanelStyle=Tinted) | notch body |
| `Surface1` | neutral T10 | cards (media card, stats cards, today cards) |
| `Surface2` | neutral T15 | inactive tiles, text fields, list rows background |
| `Surface3` | neutral T20 | chips, inactive slider track, icon containers on inactive tiles, hover of Surface2 |
| `Surface4` | neutral T25 | pressed / highest |
| `OnSurface` / `OnSurfaceVariant` / `OnSurfaceMuted` | neutral T90 / nv T80 / nv T60 | primary / secondary / tertiary text |
| `Outline` / `OutlineVariant` | nv T50 / nv T25 | borders, dividers |
| `Primary` / `OnPrimary` | P T80 / P T20 | active tiles, slider active track & handle, progress, primary buttons |
| `PrimaryContainer` / `OnPrimaryContainer` | P T30 / P T90 | emphasized containers |
| `PrimarySoft` / `OnPrimarySoft` | P T90 / P T10 | the big cream play button, today-date highlight |
| `Secondary*`, `SecondaryContainer` | S T80 / S T30 | active tab pill, selected chips, selected list row |
| `Tertiary*` | T T80 … | accents (GPU gauge, event bars) |
| `Error*`, `Success`, `Warning`, `Info`, `Pink` | harmonized | states, RAM gauge (`Success`), event color bar (`Pink`) |
| `Gauge.Cpu/Gpu/Ram` | Primary / Tertiary / Success | stats rings |
| `StateHover` / `StatePressed` | OnSurface 8 % / 12 % | state layers on hover / press |
| `Scrim` | black 60 % | behind sheets |

Content on an active (Primary) surface uses `OnPrimary`; secondary text there uses `OnPrimary` at 75 % opacity.

## 2. Typography

Fonts: **NH Sans** = Google Sans Flex (static instances 400/500/600/700/800), **NH Sans Rounded** (ROND=100; 500/700/800)
for big numerals, **NH Symbols** / **NH Symbols Filled** = Material Symbols Rounded. Keys: `Font.Sans`, `Font.SansRounded`.
The window sets `FontFamily="{DynamicResource Font.Sans}"`, so plain TextBlocks inherit NH Sans.

TextBlock styles (keys in `Typography.xaml`, all `TextTrimming=CharacterEllipsis` except display):

| key | font / weight / size | example |
|---|---|---|
| `Text.Display` | Rounded ExtraBold 56, line height 56 | clock "09" "41" |
| `Text.Headline` | Rounded Bold 30 | "85" Mbps, "32°C" |
| `Text.Number` | Rounded Bold 22 | gauge "37%" |
| `Text.TitleL` | SemiBold 17 | media title in cards, section headers |
| `Text.Title` | SemiBold 14 | tile titles, card titles ("CPU", "This week") |
| `Text.Body` | Regular 13 | normal text |
| `Text.BodyS` | Regular 12, OnSurfaceVariant | subtitles ("Home 5G", "lofi radio") |
| `Text.Label` | Medium 12 | chips, buttons, tabs |
| `Text.LabelS` | Medium 11, OnSurfaceVariant | captions, times, key hints |
| `Text.Overline` | SemiBold 11, OnSurfaceMuted | group headers ("PINNED", "TODAY") — write them uppercase in strings |

## 3. Shape

Corner radius scale (`Radius.*`): XS 8 · S 12 · M 16 · L 20 · XL 24 · XXL 28 · Full. Cards 24, tiles 20 (inactive) → full pill
(active), chips 10, text fields 16, list rows 14, sheets 28, the expanded notch bottom corners 32.
**Shape morph**: pressable surfaces squish their radius on press (e.g. pill → 12) with a fast spring and spring back on release.

## 4. Motion (springs)

`NotchHub.UI.Animation` provides `SpringEase` (an `IEasingFunction` built from damping ratio + stiffness, duration derived
from the settle time) and presets (M3 Expressive tokens):

| preset | damping | stiffness | ≈ duration | use |
|---|---|---|---|---|
| `Springs.SpatialFast` | 0.6 | 800 | 350 ms | small elements moving / resizing, press squish, tab indicator |
| `Springs.SpatialDefault` | 0.8 | 380 | 500 ms | notch morph, panel height changes |
| `Springs.SpatialSlow` | 0.8 | 200 | 650 ms | large entrances |
| `Springs.EffectsFast` | 1.0 | 3800 | 150 ms | color / opacity of small things |
| `Springs.EffectsDefault` | 1.0 | 1600 | 200 ms | fades, color transitions |

`Motion.ReduceMotion` (namespace `NotchHub.UI.Animation`, bound to settings) collapses durations to ~0. Spatial springs overshoot (that is the expressive look);
effects never overshoot.

## 5. The notch (shell)

* Window: transparent layered WPF window (`AllowsTransparency`), topmost, no taskbar entry, 1000×660 DIP, top-center of the
  chosen monitor at y = monitor top. Fully transparent pixels are click-through; an invisible `#01000000` hover zone
  surrounds the collapsed notch.
* The body is drawn by `NotchShape`: rectangle hanging from the top edge with bottom corner radius *Rb* and two inverse
  fillets ("shoulders", radius *Rs*) where it meets the top edge — exactly like `8.webp`. Content is clipped to the body.

| state | size (W×H) | Rb / Rs | content |
|---|---|---|---|
| Collapsed | settings (200×12) | 9 / 6 | nothing |
| Collapsed + live activity (media playing) | 300×34 | 17 / 8 | album art (22 px, radius 6) left, animated 4-bar equalizer (Primary) right |
| Peek (hover) | ≈380×136 measured | 28 / 12 | mini player (`4.png`) or, without media, a glance card (time, date, weather, phone battery) |
| HUD | ≈360×56 measured | 28 / 12 | volume / brightness level, phone notification, timer finished, pair request |
| Expanded | 820×(header + content) ≤ 640 | 32 / 14 | header + tab content |

Morphs between states animate W, H, Rb, Rs with `SpatialDefault` (peek: `SpatialFast`); content cross-fades
(out: 90 ms; in: 180 ms after 60 ms, from 8 px above and 0.97 scale). Expanded panel has a soft shadow (black 35 %, blur 20).

Expanded header (height 46): left = phone status chip (`smartphone` icon, battery %, bolt when charging, notification
count badge; click → Phone tab; hidden when no phone paired); center = pill tabs (text only, `Text.Label`, active pill
`SecondaryContainer` / `OnSecondaryContainer`, indicator slides with `SpatialFast`); right = gear icon button (Settings).
Content area padding `14,4,14,14`. Panel height animates to the active tab's desired height.

**User feedback (must hold):** after start the notch is the tiny collapsed notch — no text, no icon (only the live activity
when media plays). Everything must feel tactile: press squish on every clickable, springy notch morphs, sliders that track the
finger/mouse 1:1 with a handle that reacts to grabbing, subtle overshoot on expand.

**Reserve screen space** (settings `Notch.ReserveScreenSpace` + `Notch.ReservedSpace`): registers a top-edge Windows AppBar
(SHAppBarMessage ABM_NEW / ABM_QUERYPOS / ABM_SETPOS, ABN_POSCHANGED handling, ABM_REMOVE on exit/crash/toggle-off) on the notch
monitor, spanning the monitor width, height = collapsed notch height (Notch) or live-activity height (LiveActivity), so maximized
windows end right below the collapsed notch. The reserved strip itself stays transparent (wallpaper visible) except the notch.
ABN_FULLSCREENAPP is an extra fullscreen signal. The option lives in Settings → Notch, and the Settings view is opened by the gear
button in the top-right corner of the expanded panel header.

Behavior: hover (after `HoverDelayMs`) → Peek (or Expanded per settings); click → Expanded (activates the window for
keyboard); mouse leaves for `CollapseDelayMs` → back (unless a text box has focus, a sheet/menu is open, or the panel was
opened with a hotkey); click outside (deactivate) / Esc → collapse; wheel down over notch → expand; Ctrl+Tab /
Ctrl+1..6 switch tabs. Fullscreen app in foreground → hidden.

## 6. Components (`NotchHub.UI`, xmlns `ui` = `clr-namespace:NotchHub.UI.Controls;assembly=NotchHub.UI`)

Implemented by the design-system agent; feature views use them and must not re-implement them.
All have sensible defaults bound to tokens; all animate with the springs above.

| control | kind | key API | look |
|---|---|---|---|
| `Icon` | FrameworkElement | `Glyph` (Material Symbols name), `Size`=20, `Filled`, `Foreground` (inherited) | ✓ exists |
| `QsTile` | Control | `Icon`, `ActiveIcon`, `Title`, `Subtitle`, `IsActive`, `IsBusy`, `ShowChevron`, `Command`, `CommandParameter`, `ChevronCommand` | height 64. Inactive: Surface2, radius 20, icon in 40 px Surface3 circle, title OnSurface, subtitle OnSurfaceVariant. Active: Primary fill, radius → full pill, OnPrimary content, icon circle OnPrimary 12 %. Chevron: separate 46 px segment (2 px gap) with `expand_more`, same fill, inner radius 6. Press squish. |
| `M3Slider` | RangeBase | `Icon`, `ShowValue`, `ValueText` (or formatted %), `TrackHeight`=44, `ActiveBrush`, `TrackBrush`, `SmallChange`, events `DragStarted`/`DragCompleted` | M3 Expressive slider: active track (left, Primary) and inactive track (right, Surface3), outer radius 14, inner radius 4, a 4 px wide bar handle (TrackHeight+12 tall, Primary) with 6 px gaps; handle narrows to 2 px while dragging; icon inside the active track (OnPrimary, falls back to OnSurfaceVariant when the active part is too short); value label at the right end (Text.Label); stop dot at the end. Wheel/arrow keys change by SmallChange. |
| `WavyProgressBar` | FrameworkElement | `Value` 0..1, `IsWavy`, `IsAnimating`, `Amplitude`=3, `Wavelength`=28, `Thickness`=4, `ActiveBrush`, `TrackBrush`, `IsSeekable`, event `SeekCompleted(double)`, `IsSeeking` | played part = sine wave (amplitude animates to 0 when `IsWavy` false — paused), phase flows while `IsAnimating`; gap 4 px; flat track; round caps; drag/click to seek. Renders per frame only while needed. |
| `WavyRing` | ContentControl | `Value`, `Thickness`=8, `ActiveBrush`, `TrackBrush`, `IsWavy`, `WaveCount`=10, `Amplitude`=1.6, `GapDegrees`=8, `IsIndeterminate` | circular gauge from 12 o'clock clockwise: wavy active arc + flat track arc separated by gaps; Value changes animate (SpatialDefault); content (e.g. "37%") centered. |
| `M3Button` | Button | `Variant` (Filled, Tonal, Outlined, Text), `Icon`, `Size` (S 32 / M 40 / L 52) | pill; press squish → radius 12; Filled = Primary/OnPrimary, Tonal = SecondaryContainer/OnSecondaryContainer. |
| `IconButton` | Button | `Icon`, `Variant` (Standard, Filled, Tonal, Outlined), `Size` 32/40/48, `IsSelected`, `IconFilled` | circle → squish; Standard = transparent + state layer. |
| `PlayPauseButton` | ToggleButton | `IsChecked` = playing, `Size`=48 | PrimarySoft fill, OnPrimarySoft icon; shape morphs: paused = rounded square (radius 30 %), playing = circle; icons cross-fade play_arrow ↔ pause (filled). |
| `ChipToggle` style `FilterChip` | ToggleButton style | content + optional `ui:Chip.Icon` | height 32, radius 10, Surface3 → selected SecondaryContainer with check. |
| `Switch` style | ToggleButton style key `Switch` | | 52×32 track; thumb 16 → 24 with check icon when on; Primary track when on. |
| `Checkbox` style | CheckBox style key `Checkbox` | | 20 px, radius 6; checked: Primary fill + check (OnPrimary), animated. |
| `TextField` style | TextBox style key `TextField` | attached `ui:Field.Placeholder`, `ui:Field.Icon` | Surface2, radius 16, height 44, caret Primary, focus → 2 px Primary bottom-less outline. |
| ScrollViewer (implicit style) | | | overlay 4 px rounded thumb, auto-hide. |
| `SheetHost` | ContentControl | `IsOpen`, `Title`, `SheetContent`, `SheetWidth`; Esc / scrim click closes | in-panel bottom sheet: scrim, Surface1 sheet radius 28 sliding up with SpatialDefault. Used for Wi-Fi list, Bluetooth devices, audio outputs, event editor, pairing. |
| `LoadingIndicator` | Control | `Size`=40, `Brush`, `IsActive`, `Contained` | M3 Expressive morphing-polygon loading indicator (rotating, morphing between rounded shapes). |
| `Badge` | ContentControl | content = count | Error / OnError, min 16×16, pill. |
| `Avatar` | Control | `Text`, `Image` (ImageSource), `Size`=28 | circle; letter avatar with hue derived from text, harmonized. |
| `ListRow` style | ListBoxItem style | | radius 14, padding 10,8; hover state layer; selected SecondaryContainer. |
| `Card` style | Border style | | Surface1, radius 24, padding 16. Variants `CardTight` (padding 12), `CardFlat` (Surface2). |
| `Divider` style | Border style | | 1 px OutlineVariant. |

Converters (`xmlns:conv="clr-namespace:NotchHub.UI.Converters;assembly=NotchHub.UI"`): `BoolToVisibility` (param `Invert`),
`NullToVisibility`, `InverseBool`, `BytesToImage` (byte[] → frozen BitmapImage, cached), `PathToImage`, `Format`
(ConverterParameter = format string), `TimeSpanClock` (m:ss / h:mm:ss), `RelativeTime` (localized "now / 5m ago"),
`EnumEquals` (param = enum name), `ZeroToCollapsed`, `Multiply`.

## 7. Tabs (layout = references, details in each feature task)

* **Controls** (`1.png`): row 1 = Internet (Wi-Fi/Ethernet), Bluetooth, Audio output tiles (chevrons open sheets: network list,
  device list, output devices + per-app volume); row 2 = Night Light, Dark mode (+ Brightness slider row if supported);
  volume `M3Slider`; media card (source device line, art 56, title/artist, prev / PlayPause / next, `WavyProgressBar` + times).
* **Today** (`5.png`): clock card (hours / dots / minutes, "AM • Tue 29" — 24 h in Polish), weather card; week strip
  (Mon–Sun, today = PrimarySoft pill) + next event card (Pink bar); timer card (ring `WavyRing` + h/min fields + preset chips
  1m 5m 10m 15m 20m + Start/Reset); to-do card (progress ring "1/1", checkboxes with strike-through, add field).
* **Stats** (`2.png`): three `WavyRing` gauges (CPU/GPU/RAM) with name + chips; Disk / Network / Uptime cards; Top apps list
  (icon, name, bar, value; RAM⇄CPU toggle); Internet speed card (last result + "Run speed test" button, live gauge while running).
* **Clipboard** (`6.png`): search field, filter chips (All, Text, Images, Links), list grouped Pinned / Today / Yesterday / Older,
  preview pane with metadata; keys: Enter paste, Ctrl+C copy, Ctrl+P pin, Del remove, ↑↓ move, Ctrl/Shift multi-select, Tab filter.
* **Phone** (new): device card (name, battery ring + charging, signal, actions: ring, ping, send clipboard), notifications list
  (app icon, app, title, time; click expands full text + actions + reply field; dismiss; clear all); pairing UI when unpaired.
* **Power** (`3.png`): Lock, Sleep, Restart, Shut down tiles; Restart/Shut down require a second click within 4 s
  (tile shows a countdown ring "Click again").

## 8. Iconography (Material Symbols names)

wifi, wifi_off, signal_wifi_0_bar…signal_wifi_4_bar, lan, bluetooth, bluetooth_disabled, bluetooth_connected, headphones,
speaker, volume_up, volume_down, volume_mute, volume_off, nightlight, dark_mode, light_mode, brightness_6, expand_more,
skip_previous, skip_next, play_arrow, pause, memory, developer_board, speed, hard_drive, arrow_downward, arrow_upward,
schedule, lock, bedtime, restart_alt, power_settings_new, content_copy, push_pin, search, link, image, code, description,
smartphone, battery_full/battery_5_bar…, battery_charging_full, notifications, ring_volume, send_to_mobile, settings,
close, add, check, delete, sunny, partly_cloudy_day, cloud, rainy, weather_snowy, thunderstorm, foggy, clear_night,
event, timer, check_circle, radio_button_unchecked, more_vert.
