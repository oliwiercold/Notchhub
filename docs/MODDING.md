# Mody NotchHub

> English summary at the bottom ([English](#english-summary)).

Mody dodają do NotchHub własne **karty** w panelu notcha, **kafelki** w Sterowaniu (wiersz „Mody”) i krótkie
**powiadomienia (HUD)** w notchu. Są dwa rodzaje:

| rodzaj | co piszesz | kompilacja | do czego |
|---|---|---|---|
| **widżet** (`"kind": "widget"`) | `mod.json` + `view.xaml` | nie | dane z API (JSON po HTTP) albo z lokalnego skryptu, pokazane w karcie/kafelku |
| **assembly** (`"kind": "assembly"`) | biblioteka C# (`.dll`) z klasą `INotchMod` | tak (`dotnet build`) | własna logika, timery, dostęp do usług aplikacji (media, telefon, kalendarz…) |

Przykłady: [`mods/examples`](../mods/examples) — `kursy-nbp` (widżet, API NBP), `system-script` (widżet, skrypt PowerShell),
`Pomodoro` (C#). Szablony: [`templates/widget-template`](../templates/widget-template) i
[`templates/NotchHub.ModTemplate`](../templates/NotchHub.ModTemplate).

## Gdzie leżą mody

NotchHub przy starcie przeszukuje (jeden podfolder = jeden mod z plikiem `mod.json`):

1. `%APPDATA%\NotchHub\mods\<mod>\` — mody użytkownika (wygrywają przy powtórzonym `id`),
2. `<folder aplikacji>\mods\<mod>\` — mody dołączone do instalacji.

```
%APPDATA%\NotchHub\
├─ mods\
│  ├─ kursy-nbp\         mod.json, view.xaml
│  ├─ system-script\     mod.json, view.xaml, info.ps1
│  └─ pomodoro\          mod.json, Pomodoro.dll
└─ mods-data\
   └─ pomodoro\          settings.json (IModContext.GetSetting/SetSetting) i inne pliki moda
```

Po skopiowaniu moda **uruchom NotchHub ponownie**. Lista modów, przełącznik włącz/wyłącz i ewentualne błędy ładowania są w
**Ustawienia → Mody**. Błąd jednego moda nigdy nie zatrzymuje aplikacji ani innych modów.

## mod.json

JSON z komentarzami (`//`, `/* */`) i przecinkami na końcu; nazwy pól bez rozróżniania wielkości liter.

| pole | typ | opis |
|---|---|---|
| `id` | string, **wymagane** | unikalne, małe litery/cyfry/myślniki; nazwa folderu danych |
| `name` | string | nazwa w Ustawieniach (domyślnie `id`) |
| `version`, `author`, `description` | string | informacyjne |
| `kind` | `"widget"` (domyślnie) \| `"assembly"` | rodzaj moda |
| `assembly` | string | *assembly:* nazwa DLL względem folderu moda |
| `view` | string | *widget:* plik XAML (domyślnie `view.xaml`) |
| `tab` | `{ "title", "icon" }` | *widget:* karta w panelu; `icon` = nazwa [Material Symbols](https://fonts.google.com/icons) |
| `tile` | `{ "title", "subtitle", "icon" }` | *widget:* kafelek; teksty mogą zawierać `{ścieżka}` z danych |
| `data.url` | string | *widget:* adres HTTP(S) zwracający JSON |
| `data.headers` | obiekt | *widget:* dodatkowe nagłówki HTTP (np. `Accept`) |
| `data.command` + `data.args` | string + string[] | *widget:* program uruchamiany w folderze moda; jego stdout = JSON (limit 20 s) |
| `data.intervalSeconds` | liczba | *widget:* co ile odświeżać (min. 15, domyślnie 300) |

**Odświeżanie widżetów:** bez kafelka dane pobierane są tylko, gdy karta jest widoczna (i przy pierwszym pokazaniu).
Z kafelkiem — także w tle co `intervalSeconds`, bo kafelek pokazuje bieżącą wartość. Wybieraj rozsądne interwały
(kursy NBP: 3600 s).

**Placeholdery kafelka:** `{a.b.0.c}` — segmenty oddzielone kropką, indeksy tablic jako liczby. Przykład dla odpowiedzi NBP
(tablica z jednym obiektem): `"EUR {0.rates.7.mid} zł"`. Liczby formatowane są wg języka systemu (`4.2617` → `4,2617`).
Kliknięcie kafelka widżetu otwiera jego kartę (albo odświeża dane, jeśli karty nie ma).

**Polecenia (`command`)** działają tylko, gdy w **Ustawienia → Mody** włączono „Zezwalaj widżetom na uruchamianie poleceń”.

## Widżet: ściąga z bindowania

Plik `view.xaml` jest ładowany przez `XamlReader` — **bez kodu C#** (bez `x:Class`, bez obsługi zdarzeń), tylko `Binding`,
`Command`, style i zasoby. Główny element to np. `Grid`; domyślne przestrzenie nazw są dodawane automatycznie, ale warto je
zadeklarować (podpowiedzi w edytorze). Dodatkowe:

```xml
xmlns:ui="clr-namespace:NotchHub.UI.Controls;assembly=NotchHub.UI"
xmlns:conv="clr-namespace:NotchHub.UI.Converters;assembly=NotchHub.UI"
```

`DataContext` widoku to `WidgetDataContext`:

| właściwość | typ | przykład |
|---|---|---|
| `Data` | dane JSON | obiekt → `{Binding Data.temp}`, tablica → `{Binding Data[0].rates[7].mid}` |
| `IsLoading` | bool | `Visibility="{Binding IsLoading, Converter={conv:BoolToVisibility}}"` |
| `Error` | string? | `Visibility="{Binding Error, Converter={conv:NullToVisibility}}"` |
| `UpdatedAt` | DateTime? | `Text="{Binding UpdatedAt, StringFormat='{}{0:HH:mm}'}"` |
| `Refresh` | ICommand | `<ui:IconButton Icon="refresh" Command="{Binding Refresh}" />` |

Konwersja JSON: obiekt → właściwości po nazwie (wielkość liter ma znaczenie), tablica → lista z indeksem `[n]`,
liczby → `double`, `true/false` → `bool`, `null` → `null`.

Wskazówki:

- W `<Run Text="{Binding …}">` zawsze dodaj `Mode=OneWay` (Run domyślnie wiąże dwukierunkowo, a dane są tylko do odczytu).
- Formatowanie: `StringFormat={}{0:0.00}`, `StringFormat=N1` albo `Converter={conv:Format}`.
- Brak wartości: `FallbackValue=…` / `TargetNullValue=…`.
- Kolory **zawsze** przez `{DynamicResource Brush.*}` (np. `Brush.OnSurface`, `Brush.Primary`, `Brush.PrimaryContainer`,
  `Brush.OnPrimaryContainer`, `Brush.Error`) — widżet zmienia się razem z motywem i kolorami z tapety. Nie wpisuj kolorów na sztywno.
- Szerokość karty: **792 DIP**; wysokość najlepiej ≤ ~520 (dłuższą treść włóż w `ScrollViewer`).
- Teksty: widżety nie mają systemu tłumaczeń — pisz w jednym języku (przykłady są po polsku).

## Kontrolki systemu projektowego

Widżety i mody C# używają tych samych kontrolek co aplikacja (Material 3 Expressive). Pełna lista z przykładami:
**[docs/UI-COMPONENTS.md](UI-COMPONENTS.md)**. Najczęściej przydatne:

| element | XAML |
|---|---|
| tekst | `<TextBlock Style="{DynamicResource Text.TitleL}" />` — `Text.Display`, `Text.Headline`, `Text.Number`, `Text.Title`, `Text.Body`, `Text.BodyS`, `Text.Label`, `Text.Overline` |
| karta | `<Border Style="{DynamicResource Card}">` (też `CardTight`, `CardFlat`, `Divider`) |
| ikona | `<ui:Icon Glyph="euro" Size="20" Filled="True" />` |
| przyciski | `<ui:M3Button Content="Start" Icon="play_arrow" Variant="Filled" />`, `<ui:IconButton Icon="refresh" Variant="Tonal" Size="40" />` |
| postęp | `<ui:WavyRing Value="{Binding Data.fraction}">…</ui:WavyRing>`, `<ui:WavyProgressBar Value="…" />`, `<ui:LoadingIndicator IsActive="…" />` |
| inne | `ui:QsTile`, `ui:M3Slider`, `ui:Badge`, `ui:Avatar`, style `FilterChip`, `Switch`, `TextField`, `ListRow` |
| konwertery | `conv:BoolToVisibility`, `conv:NullToVisibility`, `conv:InverseBool`, `conv:Format`, `conv:TimeSpanClock`, `conv:RelativeTime`, `conv:ZeroToCollapsed` |

## Mody C# (assembly)

Projekt: skopiuj [`templates/NotchHub.ModTemplate`](../templates/NotchHub.ModTemplate) (biblioteka `net10.0-windows10.0.22621.0`,
`UseWPF`, referencje `NotchHub.Core.dll` i `NotchHub.UI.dll` z zainstalowanego NotchHub przez `HintPath`, `Private=false`).

```powershell
dotnet build -c Release -p:NotchHubDir="C:\ścieżka\do\NotchHub"
# skopiuj bin\Release\net10.0-windows10.0.22621.0\{MyMod.dll, mod.json} do %APPDATA%\NotchHub\mods\my-mod\
```

W repozytorium NotchHub zamiast `HintPath` użyj `ProjectReference` do `src/NotchHub.Core` i `src/NotchHub.UI`
(jak [`mods/examples/Pomodoro/Pomodoro.csproj`](../mods/examples/Pomodoro/Pomodoro.csproj)).

Każda publiczna klasa z bezparametrowym konstruktorem implementująca `INotchMod` zostaje utworzona i zainicjowana:

```csharp
public sealed class MyMod : INotchMod
{
    public void Initialize(IModContext ctx)          // raz, na wątku UI, po załadowaniu motywu
    {
        ctx.AddTab(new ModTab { Id = "my-mod", Title = "Mój mod", Icon = "extension", CreateView = () => new MyView() });
        var tile = new ModTile { Id = "my-mod.tile", Title = "Mój mod", Icon = "bolt",
                                 OnClick = () => ctx.ShowHud("Kliknięto!"), OnChevron = () => ctx.OpenTab("my-mod") };
        ctx.AddTile(tile);
        tile.Subtitle = "gotowe";                      // właściwości kafelka są obserwowalne — zmieniaj w dowolnej chwili (wątek UI)
    }
    public void Shutdown() { }                         // przy zamykaniu aplikacji (opcjonalne)
}
```

### API

| element | opis |
|---|---|
| `IModContext.ModId` / `ModDirectory` / `DataDirectory` | id z mod.json; folder moda (zasoby); zapisywalny folder `%APPDATA%\NotchHub\mods-data\<id>` |
| `AddTab(ModTab)` | karta po kartach wbudowanych |
| `AddTile(ModTile)` | kafelek w wierszu „Mody” w Sterowaniu |
| `OpenTab(tabId)` | rozwija panel i wybiera kartę moda |
| `ShowHud(text, duration?)` | krótki komunikat w notchu |
| `GetSetting<T>(key, fallback)` / `SetSetting<T>(key, value)` | proste ustawienia (JSON w `DataDirectory`) |
| `Log(message)` | wpis w logu aplikacji z prefiksem `[mod:<id>]` |
| `ModTab` | `Id`, `Title`, `Icon`, `CreateView` (wywoływane leniwie, raz) |
| `ModTile` | `Id`, `Title`, `Subtitle`, `Icon`, `IsActive` (obserwowalne), `OnClick`, `OnChevron` (null = bez strzałki) |
| `ITabView` (`NotchHub.UI`) | widok karty może implementować `OnShown()` / `OnHidden()` — startuj/zatrzymuj pracę na żywo |
| `Loc.Language` (`NotchHub.UI.Localization`) | `"pl"` / `"en"` — do własnych tekstów PL/EN |

### Usługi aplikacji (`NotchHub.Core.AppServices`)

Mod ma dostęp do tych samych usług co wbudowane karty (interfejsy w `src/NotchHub.Core/Contracts`):
`Settings`, `Ui` (dispatcher), `Shell`, `Media`, `Audio`, `Network`, `Bluetooth`, `Display`, `Stats`, `SpeedTest`, `Power`,
`Clipboard`, `Foreground`, `Hotkeys`, `Wallpaper`, `Weather`, `Calendar`, `Todos`, `Timer`, `Phone`. Przykład:

```csharp
var title = AppServices.Media.Current?.Title;
AppServices.Media.Changed += (_, _) => AppServices.Ui.Post(Refresh);   // zdarzenia mogą przyjść z innego wątku
```

Zasady (te same co w aplikacji — [ARCHITECTURE.md](ARCHITECTURE.md)):

- **Bezczynny CPU ≈ 0.** Timery i subskrypcje tylko, gdy coś działa lub karta jest widoczna (`ITabView`). Pomodoro uruchamia
  timer 1 s tylko w trakcie odliczania.
- UI tylko na wątku UI; operacje sieciowe/dyskowe asynchronicznie.
- Kolory przez `SetResourceReference(…, "Brush.X")` / `{DynamicResource Brush.X}`, style tekstu `Text.*`.
- Nie dołączaj kopii `NotchHub.*.dll`, `CommunityToolkit.Mvvm.dll` ani WPF — są już załadowane.
- Własne zależności NuGet: skopiuj ich DLL do folderu moda (`CopyLocalLockFileAssemblies` = true tylko dla nich).

## Udostępnianie moda

1. Spakuj **folder moda** do ZIP (`mod.json` w środku folderu, np. `kursy-nbp\mod.json`). Dla moda C# tylko `mod.json`,
   Twoja DLL i ewentualne zasoby — bez `bin`/`obj`/źródeł (źródła najlepiej osobno, np. na GitHubie).
2. Odbiorca rozpakowuje ZIP do `%APPDATA%\NotchHub\mods\` i uruchamia NotchHub ponownie.
3. Podaj wersję NotchHub, z którą mod był testowany — mody C# kompilują się względem konkretnej wersji API.

## ⚠ Bezpieczeństwo

**Mod to kod uruchamiany z Twoimi uprawnieniami.** Mod C# może zrobić wszystko, co dowolny program (czytać pliki, wysyłać dane,
uruchamiać procesy). Widżet z `command` uruchamia wskazany program/skrypt. Widżet z `url` łączy się z podanym serwerem.

- Instaluj tylko mody z zaufanych źródeł i przejrzyj `mod.json` (pola `command`, `args`, `url`) oraz dołączone skrypty.
- Mody C# bez źródeł traktuj jak każdy nieznany plik `.exe`.
- Jeśli nie używasz widżetów skryptowych, wyłącz **Ustawienia → Mody → „Zezwalaj widżetom na uruchamianie poleceń”**.
- Wyłączenie moda w Ustawieniach (lub usunięcie folderu) i restart całkowicie go wyłącza.

## Testowanie (repozytorium)

`tools/harness/mods` ładuje przykłady przez prawdziwy `ModLoader` do tymczasowego folderu, sprawdza rejestr, kafelki, HUD Pomodoro,
szablon widżetu i renderuje karty do PNG:

```powershell
dotnet build mods/examples/Pomodoro/Pomodoro.csproj
dotnet build tools/harness/mods/ModsHarness.csproj
tools/harness/mods/bin/Debug/net10.0-windows10.0.22621.0/ModsHarness.exe <folder-wyjściowy>
```

---

## English summary

- **Two kinds of mods.** *Widget* mods (`"kind": "widget"`) are `mod.json` + `view.xaml` with no compiling. They show JSON from
  `data.url` (HTTP) or from `data.command` (a local program whose stdout is JSON; it needs *Settings → Mods → allow commands*).
  *Assembly* mods (`"kind": "assembly"`) are a C# DLL with public `INotchMod` classes.
- **Location.** `%APPDATA%\NotchHub\mods\<mod>\mod.json` (or `<app>\mods`). Restart the app after installing. Errors show in
  *Settings → Mods*. Per-mod data lives in `%APPDATA%\NotchHub\mods-data\<id>`.
- **mod.json.** `id` (required), `name`, `version`, `author`, `description`, `kind`, `assembly`, `view`, `tab {title, icon}`,
  `tile {title, subtitle, icon}`, and `data {url, headers, command, args, intervalSeconds ≥ 15}`. Comments are allowed.
  Tile texts accept `{path.0.to.value}` placeholders. Icons are Material Symbols names.
- **Widget bindings.** The DataContext has `Data` (`{Binding Data.x}` / `{Binding Data[0].y}`), `IsLoading`, `Error`, `UpdatedAt`
  and `Refresh`. Use `Mode=OneWay` on `Run`, use `{DynamicResource Brush.*}` for every color, and keep the width at 792 DIP. Without
  a tile, data refreshes only while the tab is visible.
- **Controls.** Use `ui:` (`NotchHub.UI.Controls`) and `conv:` (`NotchHub.UI.Converters`). See [UI-COMPONENTS.md](UI-COMPONENTS.md).
- **C# API.** `IModContext` provides `AddTab(ModTab)`, `AddTile(ModTile)`, `OpenTab`, `ShowHud`, `Get/SetSetting`, `Log`,
  `ModDirectory` and `DataDirectory`. Views can implement `ITabView` (OnShown/OnHidden). App services are on
  `NotchHub.Core.AppServices.*`. Keep idle CPU at about zero and touch the UI only on the UI thread. Start from
  `templates/NotchHub.ModTemplate` (HintPath to an installed NotchHub; build with `-p:NotchHubDir=...`).
- **Sharing.** Zip the mod folder. The recipient unzips it into `%APPDATA%\NotchHub\mods\` and restarts the app.
- **Security.** Mods run with your permissions: C# mods can do anything, and command widgets run programs. Install only mods
  you trust, and read `mod.json` and any scripts first.
