# Historia zmian

Numer wersji jest w jednym miejscu: `Directory.Build.props` (`<Version>`).
Widać go na pasku tytułu aplikacji, w oknie „O programie” i we właściwościach pliku exe.
Sekcja z numerem wersji trafia automatycznie do opisu wydania na GitHubie (patrz README → „Wydawanie nowej wersji”).

## 1.2.1 — 2026-11-09

Pierwsze właściwe wydanie wersji 1.2. Wydanie 1.2.0 na GitHubie powstało przez pomyłkę ze starego kodu (tag wskazywał
commit wersji 1.0), więc zawierało program 1.1 z numerem 1.2.0 — zostało usunięte. Zmiany względem 1.1:

### Nowe
- **Płótno rysowane przez Win2D.** Cała mapa powstaje na jednej powierzchni zamiast setek elementów XAML;
  rysowane jest tylko to, co widać, gotowe obrysy linii (także przerywanych) są zapamiętywane, a przy bardzo dużych
  widokach scena trafia do bitmapy, którą przesuwanie i zaznaczanie tylko przesuwają. Na mapie testowej z 3660
  kafelkami i 915 strzałkami przesuwanie i zaznaczanie trwają ułamek milisekundy na klatkę (wcześniej ~0,8 s).
  Przy dużym oddaleniu tekst zamienia się w kreski, a przerywane linie w ciągłe — i tak nie byłoby ich widać.
- **Druk i eksport do PDF** (Ctrl+P, Ctrl+Shift+E, przycisk „Drukuj” na pasku) z ustawieniami i podglądem strony:
  - cała mapa dopasowana do jednej kartki **albo** podział na kilka stron w wybranej skali (plakat do sklejenia),
  - papier A4/A3/A5/Letter (PDF; przy druku — z okna drukarki), orientacja automatyczna/pionowa/pozioma,
  - marginesy, tytuł mapy u góry strony (przy wielu stronach także numer strony i jej miejsce w plakacie),
  - białe tło zamiast koloru płótna (ciemne mapy są czytelne na papierze), jakość 150–600 dpi.
  PDF jest rastrowy (strony jako obrazy w wysokiej rozdzielczości) — wersja wektorowa jest w planach.
  Ostatnie ustawienia są zapamiętywane. Z linii poleceń: `--export-pdf plik.pdf`.
- **Znaczniki w kafelkach**: priorytety 1–5 (kolorowe kółka), postęp 0–100% (wykres kołowy) i 30 symboli
  (✅ ⚠️ ❓ ⭐ 🗡️ 🏰 🐉 …) — w panelu i w menu kontekstowym; dowolne inne emoji (Win+.) przez pole „inne emoji”.
  Import z XMind przenosi priorytety, postęp, flagi i gwiazdki.
- **Linki w kafelkach**: adres strony albo plik — także **inna mapa .mapo** (ścieżka zapisywana względem bieżącej
  mapy, więc folder z mapami można przenosić). **Ctrl+klik** na kafelku albo klik w niebieski znaczek otwiera link;
  mapa otwiera się w tym samym oknie, a **Alt+←** (albo strzałka „wstecz” na pasku) wraca do poprzedniej.
  Linki z XMind (`href`) trafiają tutaj zamiast do notatki.

### Poprawki
- **Komunikaty na dole okna były nieczytelne** (biały tekst na półprzezroczystym tle nad białym płótnem w ciemnym
  motywie) — mają teraz nieprzezroczyste tło z cieniem; tak samo pasek „Nowa wersja”.
- **Zaznaczanie prostokątem nie przycina się**: w trakcie przeciągania tylko podgląd na płótnie, zaznaczenie trafia
  do mapy po puszczeniu myszy, a panel właściwości przebudowuje się raz, gdy interfejs jest bezczynny
  (wcześniej przy każdym ruchu myszy budował od nowa setki kontrolek).
- **Aktualizacje**: repozytorium nazywa się teraz `dkinvoker/MapoMyslacz` (tak jak szukały wersje 1.0–1.1);
  dawna nazwa `dkinvoker/MapoMy-lacz` jest sprawdzana zapasowo.
- Emoji zapisują się w pliku `.mapo` wprost (wcześniej jako `\uD83D…`).
- Testy: 119 (znaczniki, linki, rozkład stron do druku/PDF, zapis PDF).

## 1.1 — 2026-11-08

Pierwsze wydanie na GitHubie — zawartość jak w 1.0.

## 1.0 — 2026-11-08

Pierwsza wersja.

### Nowe
- **Płótno map myśli** (WinUI 3, Native AOT): kafelki, gałęzie drzewa i strzałki z podpisami na nieskończonym
  płótnie; przesuwanie przeciąganiem pustego miejsca (także środkowym przyciskiem i spacją), powiększanie kółkiem
  w miejscu kursora, „pokaż całą mapę”, zaznaczanie prostokątem z Shift/Ctrl, Ctrl/Shift+klik.
- **Kafelki**: 7 kształtów, wypełnienie, ramka (kolor, grubość, ciągła/kreski/kropki), czcionka, kolor tekstu
  dobierany automatycznie do tła, wyrównanie, własna szerokość zawijania, notatki; edycja tekstu w miejscu
  (dwuklik, F2 albo po prostu pisanie), 11 gotowych stylów.
- **Gałęzie**: Tab — dziecko, Enter — rodzeństwo, kolory gałęzi z palety, zwijanie („+N”), przeciąganie z całą
  gałęzią (Alt — bez), „Uporządkuj” — automatyczny układ, odłączanie od rodzica.
- **Strzałki**: przeciągnięcie kółka „+” przy kafelku na inny kafelek (albo Ctrl+L), podpisy, kolory, grubość,
  wzór linii, groty z obu stron, krzywa/prosta, wyginanie przeciąganiem i uchwytami, odwracanie kierunku.
- **Panel właściwości** dla kafelków, strzałek i całej mapy (tytuł, tło płótna); menu kontekstowe.
- Cofnij/ponów dla wszystkich zmian; kopiuj/wytnij/wklej/duplikuj, także między oknami; wklejanie konspektu
  (wcięcia, punktory, nagłówki Markdown) jako gałęzi.
- **Format `.mapo`** (JSON) z naprawą uszkodzonych plików; skojarzenie z programem (dwuklik w Eksploratorze),
  ostatnio otwierane, przeciąganie plików na okno, pytanie o zapis przy zamykaniu.
- **Import z XMind** (w programie i narzędziem `tools/XmindConvert`); przekonwertowane mapy
  „Plansza ogólna” i „Plansza Sesji 1 - wioska”.
- **Eksport do PNG** (menu, Ctrl+E i z linii poleceń: `--export-png`).
- **Jeden plik exe**: starter rozpakowuje aplikację do `%LOCALAPPDATA%\MapoMyslacz` (raz na wersję) i kładzie obok
  siebie kolorowy skrypt `MapoMyslacz - odinstaluj.cmd`.
- **Aktualizacje z wydań na GitHubie**: pasek „Nowa wersja”, pobranie, sprawdzenie SHA-256, podmiana exe,
  ponowne uruchomienie z otwartą mapą; „Pomiń tę wersję”, wyłącznik w menu „?”.
- Workflow GitHub Actions budujący wydanie (x64 i ARM64) z opisem z CHANGELOG.md.
- Testy jednostkowe: 94.
