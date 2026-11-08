# MapoMyślacz — pomysły na rozwój

Lista rzeczy, które jeszcze nie są zrobione. Gotowe funkcje opisuje [CHANGELOG.md](CHANGELOG.md).
Orientacyjna ilość pracy: **[S]** mała, **[M]** średnia, **[L]** duża.

## 🧭 Praca z dużymi mapami
- **Minimapa [M]:** mały podgląd całej mapy w rogu z ramką widoku — klik przenosi w to miejsce. Przy mapach sesji
  RPG z kilkudziesięcioma postaciami bardzo przyspiesza orientację.
- **Szukaj (Ctrl+F) [S]:** pole wyszukiwania, które podświetla pasujące kafelki (tekst i notatki) i przeskakuje
  między nimi Enterem.
- **Prowadnice przyciągania [M]:** podczas przeciągania kafelek „przykleja się” do linii środków i krawędzi
  sąsiadów (z cienką linią pomocniczą). Alt wyłącza.
- **Wyrównaj / rozłóż równo [S]:** dla zaznaczonych kafelków — do lewej, do środka, w równych odstępach.
- **Ramki / grupy tematyczne [M]:** półprzezroczysty prostokąt z nagłówkiem za grupą kafelków („Wioska”, „Las”),
  przesuwany razem z zawartością (XMind ma to jako „granice”).

## ✍️ Treść kafelków
- **Obrazki w kafelkach [M]:** wklejenie/przeciągnięcie obrazka (osadzany w pliku jako base64 albo obok mapy).
  Import z XMind mógłby wtedy przenosić też obrazki.
- **Ikony i znaczniki [S]:** zestaw emoji/ikon (priorytet, postęp, ✔, ⚠, 🗡️, 🏰) przypinanych do kafelka.
- **Linki [S]:** adres URL albo inny plik `.mapo` w kafelku — Ctrl+klik otwiera (mapy połączone w kampanię).
- **Proste formatowanie tekstu [M]:** pogrubienie/kursywa fragmentu, listy punktowane w kafelku i w notatce.
- **Zmiana rozmiaru kafelka myszą [S]:** uchwyt na krawędzi ustawiający szerokość zawijania (`width` już jest w formacie).

## 🔗 Strzałki
- **Punkty pośrednie w edytorze [M]:** dodawanie/przesuwanie punktów `via` myszą (format i rysowanie już je obsługują,
  dziś pochodzą tylko z importu XMind).
- **Strzałki łamane [S]:** trzeci rodzaj linii — pod kątem prostym (schematy, drzewa genealogiczne).
- **Kotwice [S]:** przypinanie końca strzałki do wybranej krawędzi kafelka zamiast automatycznego punktu.

## 🎨 Wygląd
- **Motywy map [M]:** zestawy kolorów całej mapy (pastelowy, ciemny „noc”, pergamin do RPG, wysoki kontrast) — zmiana
  jednym kliknięciem; style kafelków mogłyby odwoływać się do kolorów motywu zamiast stałych wartości.
- **Własne gotowe style [S]:** „Zapisz styl zaznaczonego kafelka jako gotowy” — przechowywane w ustawieniach
  albo w samej mapie (wtedy wędrują razem z plikiem).
- **Siatka kropek na płótnie [S]:** delikatne tło pomagające układać kafelki (z przyciąganiem do siatki).
- **Animacje [S]:** płynne przesuwanie widoku przy „Pokaż całą mapę” i przechodzeniu strzałkami.

## 📄 Pliki i wymiana
- **Autozapis i odzyskiwanie po awarii [S]:** kopia co minutę w `%LOCALAPPDATA%\MapoMyslacz\autosave`, propozycja
  przywrócenia po nieoczekiwanym zamknięciu.
- **Eksport SVG i PDF [M]:** wektorowo, idealnie ostro do druku (rdzeń ma już całą geometrię strzałek i kształtów).
- **Eksport/import konspektu [S]:** zapis mapy jako Markdown z nagłówkami/listą (wklejanie konspektu już działa).
- **Import z FreeMind/Freeplane (.mm) [M]:** to prosty XML — kolejny popularny format map.
- **Kilka arkuszy w jednym pliku [M]:** zakładki nad płótnem, jak w XMind (np. osobny arkusz na każdą sesję gry).
- **Miniatury `.mapo` w Eksploratorze [L]:** shell extension rysujący podgląd (wymaga osobnej biblioteki COM).

## 🧑‍🤝‍🧑 Wygoda
- **Tryb prezentacji [M]:** pełny ekran, przechodzenie po gałęziach strzałkami, odsłanianie kolejnych kafelków
  (np. prowadzenie sesji RPG albo prezentacja pomysłu).
- **Fokus na gałęzi [S]:** „pokaż tylko tę gałąź” — reszta przygaszona.
- **Ostatnie mapy na ekranie startowym [S]:** miniatury zamiast pustej mapy przy uruchomieniu bez pliku.
- **Wersja angielska [M]:** teksty interfejsu w zasobach `.resw` zamiast w kodzie.
- **Obsługa dotyku i pióra [M]:** szczypanie do powiększania, przesuwanie dwoma palcami, długie przytrzymanie = menu.

## 🛠️ Technika
- **Podpisywanie exe [S–M]:** certyfikat (np. Azure Trusted Signing) — mniej ostrzeżeń SmartScreen przy pobieraniu.
- **Aktualizacje różnicowe [M]:** dziś pobierany jest cały exe (~30 MB); można by pobierać tylko zmienioną aplikację
  (paczka z warstwą Windows App SDK zmienia się rzadko).
- **Rysowanie przez Win2D/Composition [L]:** przy tysiącach kafelków elementy XAML zaczną zwalniać — rysowanie
  na jednej powierzchni (z wirtualizacją poza ekranem) skaluje się lepiej.
- **Testy interfejsu [M]:** automatyczne testy płótna (np. WinAppDriver/UI Automation) dla przeciągania i edycji.
