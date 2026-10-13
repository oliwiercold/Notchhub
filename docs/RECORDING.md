# Scenariusz nagrań na stronę (OpenScreen)

Cel: 8 krótkich, zapętlonych klipów (5–12 s) do slotów w `website/media/`. Każdy klip pokazuje **jedną rzecz**.
Gotowy plik wrzucasz pod podaną nazwą + klatkę-plakat `.jpg` o tej samej nazwie — strona podmieni placeholder sama.

## 1. Przygotowanie (raz)

1. **Tryb ciemny Windows**: Ustawienia → Personalizacja → Kolory → *Ciemny*. Akcent: ciepły pomarańcz/bursztyn.
2. **Tapeta**: spokojna, ciepła, bez tekstu i twarzy (np. rozmyty zachód słońca, kawa, drewno). Notch ma się odcinać —
   unikaj czystej czerni u góry ekranu.
3. **Czysty pulpit**: ukryj ikony (PPM na pulpicie → Widok → odznacz *Pokaż ikony pulpitu*), zamknij komunikatory,
   włącz *Nie przeszkadzać*, ukryj zbędne ikony w zasobniku. Pasek zadań może zostać — wygląda naturalnie.
4. **Tryb demo** (żadnych prywatnych danych na nagraniu). W `Win+R` albo terminalu:
   ```
   NotchHub.exe --demo --data %TEMP%\nh-demo
   ```
   Przed nagraniem zamknij „prawdziwy” NotchHub z zasobnika, żeby nie było dwóch notchy.
5. **OpenScreen — ustawienia nagrywania**:
   * źródło: cały ekran 2560×1080, 60 fps;
   * kursor: **podświetlenie kursora** włączone (miękkie, ciepłe kółko), efekt kliknięcia włączony, rozmiar kursora +1;
   * kadr: po nagraniu przytnij do obszaru notcha w **16:9**, wyśrodkowanego w poziomie i przyklejonego do góry ekranu
     (na 2560×1080 zwykle wystarczy ok. 1280×720 od górnej krawędzi; dla zakładek z dużym panelem — 1600×900);
   * zoom/auto-zoom: wyłączony albo bardzo delikatny — sam kadr wystarczy.
6. **Skala**: zostaw skalowanie Windows jak na co dzień; jeżeli notch na kadrze wygląda za mało, zwiększ kadr, nie skalę.

## 2. Jak prowadzić kursor

* Ruszaj się **wolniej, niż Ci się wydaje** — ok. 1/3 normalnej prędkości. Prosto, bez zygzaków.
* Przed każdym kliknięciem **zatrzymaj kursor na ~0,5 s** — widz musi zobaczyć, gdzie klikasz.
* Po kliknięciu nie uciekaj kursorem — **daj animacji się skończyć** (ok. 0,8 s).
* Zaczynaj i kończ klip w **tym samym stanie** (zwykle: notch zwinięty, kursor w tym samym miejscu poniżej) — wtedy klip
  się zapętla bez szwu.
* Nagraj każdy klip 2–3 razy, wybierz najpłynniejszy. Na początku i końcu zostaw 1 s zapasu do przycięcia.

## 3. Klipy

Kursor startuje ok. 200 px pod notchem, na środku ekranu (dalej: „pozycja startowa”).

### Klip 1 — `animacje.mp4` (08 Animacje) · 6–8 s
1. Pozycja startowa, notch zwinięty — odczekaj 1 s.
2. Powoli najedź na notch — poczekaj 1 s na **podgląd** (peek).
3. Kliknij notch — panel się **rozwija**. Odczekaj 1,5 s.
4. Odjedź kursorem w dół poza panel (albo `Esc`) — notch się **zwija**.
5. Wróć do pozycji startowej, 1 s pauzy. Koniec = początek.

### Klip 2 — `sterowanie.mp4` (01 Sterowanie) · 8–10 s
1. Pozycja startowa → najedź i kliknij notch → zakładka **Sterowanie**.
2. Kliknij kafelek **Wi-Fi** (wyłącz) → po 0,8 s kliknij ponownie (włącz) — widać sprężyste morfowanie kafelka.
3. Przeciągnij suwak **głośności** powoli w prawo i z powrotem do połowy.
4. Przeciągnij suwak **jasności** w górę o kawałek.
5. Najedź na kartę odtwarzacza — pokaż falujący pasek postępu (1,5 s bez ruchu).
6. Zwiń notch i wróć do pozycji startowej.

### Klip 3 — `dzis.mp4` (02 Dziś + pasek spotkań) · 8–12 s
1. Rozwiń notch → kliknij zakładkę **Dziś**. Odczekaj 1,5 s (pogoda, zadania, wydarzenia).
2. Zaznacz jedno zadanie jako zrobione — 0,8 s pauzy.
3. Zwiń notch. Pokaż **pasek spotkania** (dane demo mają nadchodzące spotkanie; jeśli trzeba, użyj podglądu stanu
   *meeting* w Ustawieniach) — najedź na pasek, pokaż odliczanie i przycisk **Dołącz**, nie klikaj.
4. Odjedź do pozycji startowej.

### Klip 4 — `statystyki.mp4` (03 Statystyki) · 8–10 s
1. Rozwiń notch → zakładka **Statystyki**. Odczekaj 2 s — niech wykresy się „rozkręcą”.
2. Najedź powoli na wykres CPU, potem GPU (po 0,8 s na każdym).
3. Kliknij **Test prędkości** — pokaż pierwsze 3–4 s animacji (w trybie demo wynik jest symulowany).
4. Zwiń notch, pozycja startowa.

### Klip 5 — `schowek.mp4` (04 Schowek) · 6–9 s
1. Pozycja startowa, notch zwinięty. Naciśnij **Ctrl+Alt+V** — otwiera się schowek.
2. Wpisz w wyszukiwarkę 3–4 litery (spokojnie, ~4 znaki/s) — lista się filtruje.
3. Wyczyść wyszukiwanie, **przypnij** jeden element (pinezka) — 0,8 s pauzy.
4. Kliknij element, żeby go skopiować — widać potwierdzenie. `Esc` — zwinięcie.

### Klip 6 — `telefon.mp4` (05 Telefon / KDE Connect) · 8–10 s
1. Rozwiń notch → zakładka **Telefon** (demo pokazuje sparowany telefon, baterię, powiadomienia).
2. Najedź na kartę telefonu — 1 s (bateria, sieć).
3. Przewiń powoli listę powiadomień, rozwiń jedno.
4. Najedź na przycisk wysyłania pliku / schowka (bez klikania) — 0,8 s.
5. Zwiń notch. *Opcjonalnie*: osobne ujęcie 4 s, w którym przychodzi powiadomienie i notch sam robi „peek”.

### Klip 7 — `kalendarz.mp4` (06 Google Calendar) · 8–10 s
1. Rozwiń notch → **Dziś**, przewiń do wydarzeń kalendarza — 1 s.
2. Kliknij wydarzenie — pokazują się szczegóły; 1,5 s pauzy.
3. Zamknij szczegóły, przejdź na kolejny dzień (strzałka) i z powrotem.
4. Zwiń notch. (Ustawień OAuth **nie** nagrywaj — nawet w demo nie pokazujemy pól na klucze.)

### Klip 8 — `mody.mp4` (07 Mody) · 8–12 s
1. Zainstaluj wcześniej przykładowe mody (`mods/examples`: Pomodoro, kursy-nbp) w folderze demo
   `%TEMP%\nh-demo\mods` i uruchom ponownie demo.
2. Rozwiń notch → zakładka/widżet **Pomodoro** → kliknij *Start*; 1,5 s pauzy na odliczanie.
3. Przejdź do widżetu **kursy NBP** — 1,5 s.
4. *Opcjonalnie*: Ustawienia → **Mody** — pokaż listę z przełącznikami (włącz/wyłącz jeden).
5. Zwiń notch.

## 4. Eksport

* **MP4, H.264**, 60 fps, szerokość **1920 px** (16:9 → 1920×1080), bitrate ~8–12 Mb/s (cel: plik < 6 MB).
* **Bez dźwięku** (usuń ścieżkę audio — strona i tak odtwarza wyciszone).
* **Pętla**: przytnij tak, żeby pierwsza i ostatnia klatka były prawie identyczne (ten sam stan + pozycja kursora).
  Jeśli został mały przeskok, dodaj 0,2 s przenikania na końcu.
* Nazwa pliku dokładnie jak w nagłówku klipu, do `website/media/`.

## 5. Klatka-plakat (`.jpg`)

Plakat widać, zanim wideo się załaduje (i na wolnych łączach), więc wybierz **najładniejszą klatkę**, zwykle w pełni
rozwinięty panel, bez kursora w ruchu.

* W OpenScreen: ustaw suwak na wybranej klatce → *Eksportuj klatkę / zrzut* → JPG, 1920×1080.
* Albo przez ffmpeg (klatka z 3. sekundy):
  ```
  ffmpeg -ss 3 -i website\media\sterowanie.mp4 -frames:v 1 -q:v 3 website\media\sterowanie.jpg
  ```
* Cel: < 250 KB. Nazwa = nazwa wideo z `.jpg` (np. `sterowanie.jpg`).

## 6. Szybka kontrola

Uruchom `python -m http.server 8000` w folderze `website/` i otwórz `http://localhost:8000` — każdy slot powinien
odtwarzać się sam po przewinięciu do niego, bez widocznego „skoku” na końcu pętli.
