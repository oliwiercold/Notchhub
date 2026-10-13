# Google Calendar – jednorazowa konfiguracja (dla właściciela projektu)

Cel: każdy użytkownik NotchHub klika „Zaloguj się przez Google” i gotowe – bez zakładania czegokolwiek.
Robisz to **raz**, na swoim koncie Google. Zajmuje ok. 15 minut. Strona projektu już działa:
`https://oliwiercold.github.io/Notchhub/` (wielkość liter ma znaczenie!).

## 1. Projekt
1. Wejdź na https://console.cloud.google.com i zaloguj się.
2. Na górnym pasku kliknij listę projektów → **Nowy projekt** → nazwa `NotchHub` → **Utwórz**. Upewnij się, że ten projekt jest wybrany na górze.
3. Menu ☰ → **Interfejsy API i usługi** → **Biblioteka** → wyszukaj **Google Calendar API** → **Włącz**.

## 2. Google Auth Platform (ekran zgody)
Menu ☰ → **Google Auth Platform** (jeśli pojawi się przycisk „Rozpocznij” – kliknij go i przejdź kreator).

**Branding (Marka)**
- Nazwa aplikacji: `NotchHub`
- E‑mail pomocy dla użytkowników: Twój e‑mail
- Logo: `website/media/notchhub-logo-120.png` (opcjonalnie – logo wymaga weryfikacji, można dodać później)
- Strona główna: `https://oliwiercold.github.io/Notchhub/`
- Polityka prywatności: `https://oliwiercold.github.io/Notchhub/privacy.html`
- Warunki korzystania: `https://oliwiercold.github.io/Notchhub/terms.html`
- Autoryzowane domeny: `oliwiercold.github.io`
- Dane kontaktowe dewelopera: Twój e‑mail → **Zapisz**

**Audience (Odbiorcy)**
- Typ użytkownika: **Zewnętrzny**
- Stan publikacji: kliknij **Opublikuj aplikację** → potwierdź. Ma być **„W produkcji”** (w trybie testowym Google wylogowuje co 7 dni).

**Data Access (Dostęp do danych)** → **Dodaj lub usuń zakresy** → w polu „Ręcznie dodaj zakresy” wklej:
```
https://www.googleapis.com/auth/calendar.events
https://www.googleapis.com/auth/calendar.calendarlist.readonly
openid
https://www.googleapis.com/auth/userinfo.email
```
→ **Dodaj do tabeli** → **Aktualizuj** → **Zapisz**.

## 3. Klient OAuth
**Clients (Klienci)** → **Utwórz klienta** → Typ aplikacji: **Aplikacja komputerowa (Desktop app)** → nazwa `NotchHub Desktop` → **Utwórz**.
Skopiuj **Identyfikator klienta** (`…apps.googleusercontent.com`) i **Tajny klucz klienta** (`GOCSPX-…`) i wklej je Claude'owi.
(Dla aplikacji desktopowych Google nie traktuje tego klucza jako tajnego – może trafić do kodu open source.)

Od tej chwili logowanie działa dla każdego. Do czasu weryfikacji Google pokazuje ekran „Ta aplikacja nie została zweryfikowana”
(użytkownik klika **Zaawansowane → Przejdź do NotchHub**) i obowiązuje limit 100 użytkowników.

## 4. Weryfikacja (żeby zniknęło ostrzeżenie)
1. **Domena:** https://search.google.com/search-console → **Dodaj usługę** → **Prefiks URL** → `https://oliwiercold.github.io/` →
   metoda **Plik HTML** → pobierz plik `googleXXXX.html` i daj go Claude'owi.
   Uwaga: plik musi być w katalogu głównym `https://oliwiercold.github.io/` – czyli w osobnym repozytorium o nazwie dokładnie
   `oliwiercold.github.io` (Claude je założy i wrzuci plik). Potem **Zweryfikuj**.
2. **Filmik (YouTube, niepubliczny):** 1–2 min: uruchomienie NotchHub → Ustawienia → Kalendarz → „Zaloguj się przez Google” → ekran zgody
   (widoczny pasek adresu z `client_id`) → zgoda → wydarzenia pojawiają się w zakładce „Dziś” i meeting barze → dodanie wydarzenia.
   Nagrywanie: zobacz `docs/RECORDING.md`, klip „google-verification”.
3. **Google Auth Platform → Verification Center (Centrum weryfikacji)** → **Przygotuj do weryfikacji** → uzupełnij uzasadnienia zakresów
   (Claude przygotuje teksty) + link do filmiku → **Prześlij**. Odpowiedź zwykle w ciągu kilku dni; czasem Google prosi o poprawki mailem.
