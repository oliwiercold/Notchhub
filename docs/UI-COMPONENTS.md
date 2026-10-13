# NotchHub.UI — component reference

Everything below is available once `Themes/All.xaml` is merged into `Application.Resources` (App does it).
Visual check: `dotnet build tools/gallery/Gallery.csproj --no-dependencies` then `Gallery.exe out.png [#hex] [Variant]`
(renders default tokens | generated scheme side by side).

```xml
xmlns:ui="clr-namespace:NotchHub.UI.Controls;assembly=NotchHub.UI"
xmlns:conv="clr-namespace:NotchHub.UI.Converters;assembly=NotchHub.UI"
```

Rules: brushes only via `{DynamicResource Brush.X}` (tokens in `Themes/Tokens.xaml`, DESIGN.md §1); text via the
`Text.*` styles; motion via `NotchHub.UI.Animation` (`Springs.*`, `this.Animate(...)`, honour `Motion.ReduceMotion`).

## Typography (`Style="{DynamicResource Text.X}"` on TextBlock)

| key | look |
|---|---|
| `Text.Display` | Rounded ExtraBold 56 (clock) |
| `Text.Headline` | Rounded Bold 30 |
| `Text.Number` | Rounded Bold 22 (gauge values) |
| `Text.TitleL` / `Text.Title` | SemiBold 17 / 14 |
| `Text.Body` / `Text.BodyS` | Regular 13 / 12 (BodyS = OnSurfaceVariant) |
| `Text.Label` / `Text.LabelS` | Medium 12 / 11 (LabelS = OnSurfaceVariant) |
| `Text.Overline` | SemiBold 11 OnSurfaceMuted — write the string uppercase |

Foreground is inherited for all others (set it on the container, e.g. `Foreground="{DynamicResource Brush.OnSurface}"`).

## Controls

```xml
<!-- Icon (Material Symbols name) -->
<ui:Icon Glyph="wifi" Size="20" Filled="True" />

<!-- Quick-settings tile. Command = main segment, ChevronCommand = split chevron. IsActive is NOT toggled by the tile. -->
<ui:QsTile Icon="wifi" ActiveIcon="wifi" Title="{DynamicResource controls.wifi}" Subtitle="{Binding WifiName}"
           IsActive="{Binding WifiOn}" IsBusy="{Binding WifiSwitching}" ShowChevron="True"
           Command="{Binding ToggleWifi}" ChevronCommand="{Binding OpenWifiSheet}" />
<!-- also CLR events Click / ChevronClick; Enter/Space = main, Right arrow = chevron -->

<!-- M3 Expressive slider (RangeBase: Minimum/Maximum default 0..1, SmallChange 0.05). -->
<ui:M3Slider Icon="volume_up" Minimum="0" Maximum="100" SmallChange="2" Value="{Binding Volume}"
             ShowValue="True" ValueText="{Binding VolumeText}" TrackHeight="44"
             DragStarted="OnDragStarted" DragCompleted="OnDragCompleted" />
<!-- ValueText null → "64%". IsDragging property. Brushes: ActiveBrush, TrackBrush, OnActiveBrush, Foreground (label). -->

<!-- Wavy linear progress (Value 0..1). Wave flows only while IsAnimating && visible; IsWavy=False flattens (paused). -->
<ui:WavyProgressBar Value="{Binding Progress}" IsWavy="{Binding IsPlaying}" IsAnimating="{Binding IsPlaying}"
                    IsSeekable="True" SeekCompleted="OnSeek" />
<!-- SeekCompleted: EventHandler<double> (0..1); IsSeeking is true while dragging. Amplitude=3 Wavelength=28 Thickness=4. -->

<!-- Circular wavy gauge (Value 0..1, spring-animated) with centered content -->
<ui:WavyRing Width="84" Height="84" Value="{Binding Cpu}" ActiveBrush="{DynamicResource Brush.Gauge.Cpu}">
    <TextBlock Style="{DynamicResource Text.Number}" Text="{Binding CpuText}" />
</ui:WavyRing>
<!-- Thickness=8, IsWavy, WaveCount=10, Amplitude=1.6, GapDegrees=8, TrackBrush -->

<!-- Buttons -->
<ui:M3Button Content="{DynamicResource common.save}" Icon="check" Variant="Filled" Size="M" Command="{Binding Save}" />
<!-- Variant: Filled | Tonal | Outlined | Text   Size: S (32) | M (40) | L (52) -->
<ui:IconButton Icon="skip_next" Variant="Standard" Size="40" Command="{Binding Next}" />
<!-- Variant: Standard | Filled | Tonal | Outlined; IsSelected (toggle look, set it yourself), IconFilled -->
<ui:PlayPauseButton IsChecked="{Binding IsPlaying, Mode=OneWay}" Command="{Binding PlayPause}" Size="48" />

<!-- In-panel bottom sheet: Content = page, SheetContent = sheet. Esc / scrim click sets IsOpen=False (two-way). -->
<ui:SheetHost IsOpen="{Binding WifiSheetOpen}" Title="{DynamicResource controls.wifiNetworks}" SheetWidth="NaN">
    <Grid><!-- page --></Grid>
    <ui:SheetHost.SheetContent>
        <ListBox ItemsSource="{Binding Networks}" ItemContainerStyle="{DynamicResource ListRow}" />
    </ui:SheetHost.SheetContent>
</ui:SheetHost>
<!-- also SheetContentTemplate, event Dismissed -->

<ui:LoadingIndicator Size="40" IsActive="{Binding Loading}" />            <!-- Contained="True" = with container circle -->
<ui:Badge Content="3" />                                                   <!-- empty content = 8 px dot -->
<ui:Avatar Text="{Binding DeviceName}" Image="{Binding Photo}" Size="28" /> <!-- initials + hue from text -->
```

## Keyed styles

```xml
<ToggleButton Style="{DynamicResource FilterChip}" Content="Images" ui:Chip.Icon="image" IsChecked="{Binding ImagesOnly}" />
<ToggleButton Style="{DynamicResource Switch}" IsChecked="{Binding Enabled}" />
<CheckBox Style="{DynamicResource Checkbox}" Content="{Binding Title}" IsChecked="{Binding Done}" />
<TextBox Style="{DynamicResource TextField}" ui:Field.Placeholder="{DynamicResource clipboard.search}" ui:Field.Icon="search"
         Text="{Binding Query, UpdateSourceTrigger=PropertyChanged}" />
<ListBox ItemContainerStyle="{DynamicResource ListRow}" Background="Transparent" BorderThickness="0" />
<Border Style="{DynamicResource Card}" />       <!-- Surface1, radius 24, padding 16 -->
<Border Style="{DynamicResource CardTight}" />  <!-- padding 12 -->
<Border Style="{DynamicResource CardFlat}" />   <!-- Surface2 -->
<Border Style="{DynamicResource Divider}" />    <!-- 1 px OutlineVariant -->
```

`ScrollViewer` has an implicit thin overlay style (4 px thumb, fades in on hover) — nothing to do.
Note: when a ListBox's items are UIElements (not data), set `Foreground` on the ListBox (logical-tree inheritance).

## Converters (also markup extensions — no resources needed)

```xml
Visibility="{Binding IsOn, Converter={conv:BoolToVisibility}}"                         <!-- ConverterParameter=Invert -->
Visibility="{Binding Selected, Converter={conv:NullToVisibility}}"                     <!-- null/"" → Collapsed; Invert -->
IsEnabled="{Binding IsBusy, Converter={conv:InverseBool}}"
Source="{Binding ArtBytes, Converter={conv:BytesToImage}, ConverterParameter=96}"      <!-- frozen, cached, DecodePixelWidth -->
Source="{Binding IconPath, Converter={conv:PathToImage}, ConverterParameter=48}"
Text="{Binding Gb, Converter={conv:Format}, ConverterParameter='{}{0:N1} GB'}"        <!-- or ConverterParameter=N1 -->
Text="{Binding Position, Converter={conv:TimeSpanClock}}"                              <!-- TimeSpan or seconds → m:ss / h:mm:ss -->
Text="{Binding CopiedAt, Converter={conv:RelativeTime}}"                               <!-- now / 5m ago / 3h ago / 2d ago -->
IsChecked="{Binding Mode, Converter={conv:EnumEquals}, ConverterParameter=Dark}"       <!-- two-way capable; → Visibility too -->
Visibility="{Binding Count, Converter={conv:ZeroToCollapsed}}"                         <!-- 0 / null / "" / empty collection -->
Width="{Binding ActualWidth, ElementName=X, Converter={conv:Multiply}, ConverterParameter=0.5}"
```
Code helpers: `TimeSpanClock.ToClock(ts)`, `RelativeTime.Describe(dto)`.

## Theming (`NotchHub.UI.Theming.ThemeService`)

* `ThemeService.Current` — running instance: `SourceColor`, `WallpaperCandidates` (≤ 4, for the settings picker),
  `AverageColor`, `CurrentScheme`, event `Changed` (UI thread), `RefreshAsync()`.
* Reacts to wallpaper `Changed` and to `settings.Appearance` changes (Source, WallpaperCandidateIndex, CustomColor,
  Variant, PanelStyle, Contrast) automatically.
* `ThemeService.Generate(Color source, SchemeVariant, double contrast, PanelStyle)` → token map (keys without prefix:
  `"Primary"`, `"Surface2"`, `"Gauge.Cpu"`, …) — e.g. for scheme previews in settings.
* `ThemeService.Apply(map, animate)` installs live brushes (once) and animates `Brush.*` (~300 ms) + replaces `Color.*`.
* Custom drawing code: expose Brush DPs and set them in a style via DynamicResource (as `M3Slider` does) — never cache a Color.
