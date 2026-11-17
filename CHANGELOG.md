# Historia zmian

Numer wersji jest w jednym miejscu: `Directory.Build.props` (`<Version>`).
Widać go na pasku tytułu aplikacji, w oknie „O programie” i we właściwościach pliku exe.
Sekcja z numerem wersji trafia automatycznie do opisu wydania na GitHubie (patrz README → „Wydawanie nowej wersji”).

## 1.3.1 — 2026-11-17

Pierwsze wydanie wersji 1.3. Wersja 1.3.0 (16 listopada) nie była publikowana osobno, więc ten opis obejmuje
obie części.

### Nowe w 1.3.1
- **Obrazki w kafelkach.** Wklejenie obrazka ze schowka (Ctrl+V), przeciągnięcie pliku z Eksploratora (na kafelek —
  do niego, na puste miejsce — nowy kafelek z obrazkiem) albo panel → Obrazek → „Wstaw…”. Obrazek stoi nad tekstem
  kafelka; kwadracik w rogu zmienia jego rozmiar (dwuklik — rozmiar domyślny), „Otwórz” pokazuje go w domyślnym
  programie do obrazków. Obrazki są osadzone w pliku `.mapo` (mapa zostaje jednym plikiem), duże zdjęcia zmniejszane do 1600 px,
  ten sam obrazek w kilku kafelkach zapisuje się raz. Przechodzą przez kopiuj/wklej (także do innej mapy), cofanie,
  eksport PNG, PDF i druk.
- **Arkusze — kilka map w jednym pliku.** Zakładki pod płótnem (np. osobny arkusz na każdą sesję gry): „+” albo
  Ctrl+T dodaje arkusz, Ctrl+PageDown/PageUp przełącza, dwuklik zmienia nazwę, prawy przycisk — kopia, przestawianie,
  usuwanie. Każdy arkusz pamięta swój widok i historię cofania. Starsze wersje programu otwierają z takiego pliku
  pierwszy arkusz. Import z XMind przenosi wszystkie arkusze jako zakładki (wcześniej tylko pierwszy).
- **Motywy map:** Domyślny, Pastelowy, Noc, Pergamin (do sesji RPG), Tablica, Schemat i Wysoki kontrast — kolory
  płótna, kafelków, tematu głównego i palety gałęzi jednym kliknięciem (panel mapy, karty z podglądem). Motyw
  uzupełnia tylko to, czego kafelek nie ustawia sam; „Ujednolić z motywem…” zdejmuje własne kolory (kształty,
  czcionki i wygięcia zostają).
- **Style strzałek** niezależne od motywu: przerywane łuki, ciągłe łuki, kropkowane, proste, łamane (schemat),
  kolorowe (w kolorze gałęzi kafelka, z którego wychodzą — widać, czyja to relacja) i dwukierunkowe.
- **Gałęzie da się kliknąć.** Linia od rodzica do dziecka (ta bez grotu, powstająca przez Tab albo przeciągnięcie
  „+” na puste miejsce) to gałąź drzewa, nie strzałka — dotąd nie dało się jej zaznaczyć. Teraz klik ją zaznacza:
  Delete odpina kafelek od rodzica (zostaje na miejscu jako swobodny), menu ma „Zamień na strzałkę” i kolor gałęzi.

### Poprawki w 1.3.1
- Płótno jest widoczne dla czytników ekranu i narzędzi UI Automation (wcześniej drzewo dostępności kończyło się
  na płótnie).
- Po zmianie rozmiaru okna środek widoku zostaje w miejscu (mapa nie „ucieka” w róg).
- `MAPOMYSLACZ_DATA_DIR=<folder>` przenosi ustawienia i autozapis w inne miejsce (używają tego testy interfejsu).
- Testy: 169 jednostkowych (nowe: gałęzie, obrazki, arkusze, motywy i style strzałek) oraz nowy projekt
  **testów interfejsu** (FlaUI): przeciąganie i cofanie, zaznaczanie prostokątem, Tab z wpisaniem tekstu, klik
  w gałąź, wyszukiwanie, ramka, nowy arkusz — uruchamiane na życzenie (`MAPOMYSLACZ_UI_TESTS=1`).

### Nowe w 1.3.0
- **Autozapis i odzyskiwanie po awarii.** Co minutę niezapisane zmiany trafiają do
  `%LOCALAPPDATA%\MapoMyslacz\autosave`. Po zapisaniu mapy albo poprawnym zamknięciu kopia znika; po awarii
  (albo wyłączonym prądzie) program przy starcie proponuje przywrócenie — mapa wraca jako niezapisana,
  Ctrl+S zapisze ją w miejscu oryginału.
- **Wyszukiwanie (Ctrl+F)** w tekstach i notatkach — bez wielkości liter i polskich znaków („zolw” znajdzie „Żółw”),
  kilka słów naraz. Wyniki podświetlone, Enter/F3 przechodzi po mapie gałąź po gałęzi, zwinięte gałęzie same się
  rozwijają.
- **Minimapa** w prawym dolnym rogu, gdy mapa nie mieści się w oknie — z ramką widoku; klik albo przeciągnięcie
  przenosi w to miejsce. Wyłącza się w menu **Widok** (ikona oka).
- **Fokus na gałęzi (Ctrl+Shift+F):** zaznaczony kafelek z gałęzią i wszystkim, co łączy się z nimi strzałkami —
  wyraźnie, reszta mapy przygaszona (np. postać i jej relacje). Esc wraca do całej mapy.
- **Ramki wokół grup kafelków (Ctrl+G)** — półprzezroczysty prostokąt z nazwą („Wioska”, „Las”). Ramka sama obejmuje
  swoje kafelki z gałęziami i jedzie razem z nimi; chwycona za nagłówek przesuwa całą grupę. Kolor, nazwa, dodawanie
  i wyjmowanie kafelków w menu kontekstowym. Granice z XMind importują się jako ramki.
- **Wyrównywanie:** kilka zaznaczonych kafelków → do lewej/prawej/góry/dołu, do środka, rozłożenie w równych odstępach
  (menu kontekstowe i panel).
- **Linie pomocnicze przy przeciąganiu:** kafelek przyciąga się do krawędzi i środków sąsiadów (czerwone linie);
  Ctrl wyłącza.
- **Uchwyt szerokości** w rogu zaznaczonego kafelka — przeciągnięcie ustawia, gdzie zawija się tekst
  (dwuklik — z powrotem automatycznie).
- **Strzałki łamane** (pod kątem prostym, z zaokrąglonymi narożnikami) — trzeci kształt obok łuku i prostej; kafelek
  pod kafelkiem dostaje łącznik jak w drzewie genealogicznym. Przeciągnięcie przesuwa środkowy odcinek.
- **Linki do kafelków:** `#id` (kafelek w tej samej mapie) i `Sesja 2.mapo#id` (kafelek w innej mapie — po otwarciu
  widok płynnie przesuwa się do niego). Panel → Link → „Kafelek…” i klik; prawy przycisk → „Kopiuj link do kafelka”.
- **Linki zwrotne:** panel mapy pokazuje, które mapy z tego samego folderu prowadzą do tej („Sesja A — „Brama””),
  klik otwiera je przy właściwym kafelku.
- **Konspekt Markdown:** eksport (tematy jako nagłówki, gałęzie jako listy, notatki jako cytaty, linki, znaczniki,
  strzałki i ramki w osobnych sekcjach) i import `.md`/`.txt` (Plik albo upuszczenie na okno) — eksport i import
  dają z powrotem tę samą strukturę.
- **Własne gotowe style:** Styl → „Zapisz styl zaznaczonego kafelka jako mój” — dostępne we wszystkich mapach
  (menu Styl, panel, menu kafelka).
- **Siatka kropek** na płótnie (menu Widok) — kropki zawsze tej samej wielkości, rzedną przy oddalaniu.
- **Płynne przejścia widoku:** „Pokaż całą mapę”, przyciski powiększenia, przechodzenie strzałkami, wyszukiwanie
  i linki przesuwają widok animacją zamiast skoku.

### Poprawki i wydajność w 1.3.0
- **Przeciąganie na ogromnych mapach**: nieruchoma część sceny trafia na czas przeciągania do bitmapy, a w każdej
  klatce rysowane jest tylko to, co się rusza (kafelki z gałęziami, wyginana strzałka, kafelek o zmienianej szerokości).
  Na mapie testowej z 3660 kafelkami klatka przy przeciąganiu: ~200 ms → 12–19 ms.
- Kliknięcie kafelka i od razu przeciągnięcie nie przycina się na początku: panel właściwości buduje się dopiero
  po puszczeniu przycisku myszy (wcześniej jego budowa i układ potrafiły zatrzymać pierwsze ruchy na ponad sekundę).
- Minimapa nie przerysowuje się w trakcie przeciągania (odświeża się po puszczeniu myszy).
- Testy: 152 (autozapis, wyszukiwanie, wyrównywanie, ramki, konspekt Markdown, strzałki łamane, linki do kafelków
  i linki zwrotne).

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
