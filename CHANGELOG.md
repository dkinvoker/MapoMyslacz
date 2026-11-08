# Historia zmian

Numer wersji jest w jednym miejscu: `Directory.Build.props` (`<Version>`).
Widać go na pasku tytułu aplikacji, w oknie „O programie” i we właściwościach pliku exe.
Sekcja z numerem wersji trafia automatycznie do opisu wydania na GitHubie (patrz README → „Wydawanie nowej wersji”).

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
