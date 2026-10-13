# Wideo na stronę

Wrzuć tutaj pliki o dokładnie tych nazwach - strona sama je podmieni (placeholder znika po załadowaniu):

| Plik | Sekcja |
|---|---|
| `sterowanie.mp4` + `sterowanie.jpg` | 01 Sterowanie |
| `dzis.mp4` + `dzis.jpg` | 02 Dziś + pasek spotkań |
| `statystyki.mp4` + `statystyki.jpg` | 03 Statystyki |
| `schowek.mp4` + `schowek.jpg` | 04 Schowek |
| `telefon.mp4` + `telefon.jpg` | 05 Telefon / KDE Connect |
| `kalendarz.mp4` + `kalendarz.jpg` | 06 Google Calendar |
| `mody.mp4` + `mody.jpg` | 07 Mody |
| `animacje.mp4` + `animacje.jpg` | 08 Animacje |

Format: MP4 H.264, 16:9, ~1920 px szerokości, 60 fps, bez dźwięku, zapętlalne, najlepiej < 6 MB.
`.jpg` to klatka-plakat (poster) tej samej wielkości. Jak nagrać: `docs/RECORDING.md`.
Nowy slot = nowy `<figure class="vid" data-video="media/nazwa.mp4">` w `index.html`.
