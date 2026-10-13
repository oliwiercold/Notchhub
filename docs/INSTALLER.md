# Instalator / Installer

## Polski

**NotchHub-Setup-&lt;wersja&gt;.exe** to instalator w stylu aplikacji: na górze ekranu pojawia się czarny notch, który
rozwija się w kartę „Zainstalować NotchHub?” z przyciskami **Zainstaluj** i **Opcje**.

* Instalacja dla bieżącego użytkownika, **bez uprawnień administratora**: `%LOCALAPPDATA%\Programs\NotchHub`
  (`NotchHub.exe`, `Assets\app.ico`, `Uninstall.exe`).
* **Opcje** (wciąż w notchu, przewijane): język (Polski / English), wygląd (źródło koloru: kolory z tapety / średni kolor
  tapety / akcent Windows / własny kolor z próbnikami; styl palety; animacje Klasyczne / Odświeżone / Płynne — podgląd na
  żywo w instalatorze), uruchamianie z Windows, zachowanie po najechaniu (podgląd / od razu panel / tylko klik),
  rezerwowanie miejsca na ekranie (wyłączone / notch / dynamicznie), telefon przez KDE Connect, historia schowka, pasek
  spotkań, skróty klawiszowe (Ctrl+Alt+Spacja / Ctrl+Alt+V), przykładowe widżety, skrót na pulpicie i w menu Start, uruchomienie po instalacji, folder instalacji.
* Ustawienia są zapisywane do `%APPDATA%\NotchHub\settings.json` **tylko wtedy, gdy ten plik jeszcze nie istnieje**.
  Aktualizacja (lub ponowna instalacja) zachowuje ustawienia użytkownika i podmienia tylko pliki programu.
* Jeśli NotchHub działa, instalator prosi o jego zamknięcie (WM_CLOSE, po 4 s zakończenie procesu).
* Pliki są najpierw rozpakowywane do folderu tymczasowego obok docelowego i podmieniane zmianą nazwy folderu (z
  wycofaniem przy błędzie) — przerwana instalacja nie zostawia połowicznie zapisanego programu.
* Rejestracja w „Aplikacje i funkcje” (HKCU), autostart przez `HKCU\...\Run` (wartość `NotchHub`, ta sama co w aplikacji).
* Dziennik: `%TEMP%\NotchHub-setup.log`.
* **Odinstalowanie:** Ustawienia Windows → Aplikacje → NotchHub → Odinstaluj (uruchamia `Uninstall.exe --uninstall`).
  Ten sam notch pyta „Odinstalować NotchHub?”, z opcją „Usuń też moje dane”. Usuwane są pliki programu, skróty, wpis w
  rejestrze i autostart; `Uninstall.exe` usuwa się sam chwilę po zamknięciu.
* Po instalacji karta „Gotowe!” z przyciskami **Zamknij** (zamyka bez uruchamiania) i **Uruchom**. Gdy zaznaczono
  „Uruchom po instalacji”, po chwili dzieje się to samo co po **Uruchom**: karta zwija się z powrotem w zwykły notch, znika,
  okno instalatora się zamyka i dopiero wtedy startuje NotchHub - jego notch pojawia się w tym samym miejscu.
* Esc anuluje (przed rozpoczęciem instalacji).

**Wymagania:** Windows 10 2004+ / Windows 11 x64 oraz
[.NET 10 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/10.0). Instalator i aplikacja są
„framework-dependent” — jeśli środowiska brakuje, Windows pokaże okno z linkiem do pobrania.

Wersja przenośna: `NotchHub-<wersja>-portable.zip` — rozpakuj i uruchom `NotchHub.exe` (dane i tak trafiają do
`%APPDATA%\NotchHub`).

## English

**NotchHub-Setup-&lt;version&gt;.exe** looks like the app: a black notch appears at the top-center of the screen and springs
open into a "Install NotchHub?" card with **Install** and **Options**.

* Per-user install, **no admin rights**: `%LOCALAPPDATA%\Programs\NotchHub`.
* **Options** (inside the notch, scrollable): language, appearance (color source, swatches, palette style, animation
  style — previewed live), start with Windows, hover behaviour, reserved screen space, phone (KDE Connect), clipboard
  history, meeting bar, keyboard shortcuts, example widgets, desktop / Start menu shortcuts, launch after install, install folder.
* `%APPDATA%\NotchHub\settings.json` is written **only when it does not exist yet**; upgrades keep the user's settings and
  only replace the program files.
* A running NotchHub is closed first (asks; WM_CLOSE, killed after 4 s). Files are staged next to the target and swapped in
  with folder renames (rolled back on failure). HKCU uninstall entry, HKCU `Run` value `NotchHub` for autostart.
* Uninstall: Settings → Apps → NotchHub (`Uninstall.exe --uninstall`), optional "also remove my data"; `Uninstall.exe`
  deletes itself shortly after it exits. Log: `%TEMP%\NotchHub-setup.log`. Esc cancels before installing starts.
* Finish: "Gotowe!" card with **Close** (no launch) and **Launch**. With "launch after install" it launches on its own after
  a moment. Launching folds the card back into the plain notch, fades it out, closes the setup window and only then starts
  the app, so the app's notch takes over in the same place (no two stacked notches).
* Requires the **.NET 10 Desktop Runtime (x64)** (both the setup and the app are framework-dependent).

## For developers

```powershell
pwsh tools/build-release.ps1          # → artifacts/NotchHub-Setup-<v>.exe, NotchHub-<v>-portable.zip, SHA256SUMS.txt
```

The script publishes `NotchHub.App` single-file (framework-dependent, win-x64, Release, version from
`Directory.Build.props`), then publishes `src/NotchHub.Installer` single-file with `-p:NotchHubPayload=<NotchHub.exe>`;
the payload is zipped at build time and embedded as the `NotchHub.Payload.zip` manifest resource (default payload path:
`publish/single/NotchHub.exe`). A missing payload fails Release builds / publishes with a clear message; Debug builds only
warn (so a solution build on a clean clone still works) and that setup refuses to install. Example widget mods from
`mods/examples` (no-code ones only) are embedded too and copied to `%APPDATA%\NotchHub\mods` on a fresh install.

Pushing a tag `v<version>` (must equal `<Version>` in `Directory.Build.props`) runs `.github/workflows/release.yml`, which
builds the same artifacts on `windows-latest` and creates a GitHub release with both files and `SHA256SUMS.txt`.

**Test mode** (never touches the registry, shortcuts or autostart, never starts the app, only closes NotchHub instances
started from the target folder):

```powershell
NotchHub-Setup.exe --dry-run --target $env:TEMP\nh\app --data $env:TEMP\nh\data            # prompt
NotchHub-Setup.exe --dry-run --target ... --data ... --page options                         # options page
NotchHub-Setup.exe --dry-run --target ... --data ... --page done --no-launch --auto-close 6 # installs, shows "Gotowe!"
NotchHub-Setup.exe --dry-run --target ... --data ... --page done                            # installs, folds away, logs "[dry-run] would start"
$env:TEMP\nh\app\Uninstall.exe --uninstall --dry-run --target ... --data ... [--page done] [--remove-data]
```

Other switches: `--lang pl|en`, `--page options-end` (options scrolled to the bottom), `--auto-close <s>`, `--no-launch` ("launch after install" preset off).
