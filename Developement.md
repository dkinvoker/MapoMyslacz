# MapoMyślacz — pomysły na rozwój

Lista rzeczy, które jeszcze nie są zrobione. Gotowe funkcje opisuje [CHANGELOG.md](CHANGELOG.md).
Orientacyjna ilość pracy: **[S]** mała, **[M]** średnia, **[L]** duża.

## ⭐ Następne w kolejce
- **Arkusze — cały plik naraz [S–M]:** wyszukiwanie (Ctrl+F), druk/PDF i konspekt Markdown działają dziś na bieżącym
  arkuszu — opcja „wszystkie arkusze” (wyniki z nazwą arkusza, PDF z arkuszem na stronę, konspekt z nagłówkiem na
  arkusz). Do tego przeciągnięcie kafelków na zakładkę innego arkusza (dziś: wytnij i wklej).
- **Obrazki — dalsze kroki [S–M]:** import obrazków z XMind (dziś informacja o obrazku trafia do notatki), opcja
  „trzymaj obok mapy” dla dużych zdjęć (plik w folderze mapy zamiast base64), obrazek obok tekstu zamiast nad nim,
  przycinanie.

## 🗺️ Kampania (mapy połączone linkami)
- **Podgląd linkowanej mapy [S]:** po najechaniu na znaczek linku — miniatura mapy docelowej (scena Win2D jak
  w minimapie).
- **Szukanie we wszystkich mapach folderu [M]:** Ctrl+Shift+F w trybie „kampania” — wyniki z wielu map, klik
  otwiera mapę przy kafelku (linki zwrotne już przeglądają folder w tle).
- **Eksport kampanii do Obsidiana [S]:** konspekty Markdown wszystkich map z linkami `[[Sesja 2]]` między nimi.

## 🧭 Praca z mapą
- **Znajdź i zamień [S]:** pole „zamień na” w wyszukiwarce (jeden krok cofania dla wszystkich zamian).
- **Ramki: przeciągnięcie kafelka do ramki [S]** dodaje go do niej (dziś: menu „Dodaj do ramki”), ramki zagnieżdżone,
  zwijanie ramki do samego nagłówka.
- **Gałęzie łamane [S]:** styl gałęzi drzewa „pod kątem prostym” (jak strzałki łamane) — drzewa genealogiczne
  i schematy organizacyjne bez rysowania strzałek.
- **Panel właściwości szybciej [S]:** przy pierwszym zaznaczeniu panel buduje kilkadziesiąt kontrolek (znaczniki,
  palety) — można je tworzyć raz i tylko odświeżać.

## ✍️ Treść kafelków
- **Proste formatowanie tekstu [M]:** pogrubienie/kursywa fragmentu, listy punktowane w kafelku i w notatce.

## 🔗 Strzałki
- **Punkty pośrednie w edytorze [M]:** dodawanie/przesuwanie punktów `via` myszą (format i rysowanie już je obsługują,
  dziś pochodzą tylko z importu XMind).
- **Kotwice [S]:** przypinanie końca strzałki do wybranej krawędzi kafelka zamiast automatycznego punktu.

## 🎨 Wygląd
- **Własne motywy [S]:** „zapisz kolory tej mapy jako motyw” (jak własne gotowe style) — dostępny we wszystkich mapach.
- **Motywy: czcionka, ramki i gałęzie [S]:** krój pisma w motywie (np. odręczny do „Tablicy”), kolory ramek, grubość
  gałęzi drzewa.
- **Przyciąganie do siatki [S]:** siatka kropek już jest — przy włączonej środki kafelków mogłyby do niej przyskakiwać.

## 📄 Pliki i wymiana
- **Wektorowy PDF i SVG [M–L]:** dziś PDF i druk są rastrowe (strony jako obrazy 150–600 dpi — ostre, ale pliki są
  większe, a tekstu nie da się zaznaczyć). Kształty i krzywe przeniesie się łatwo (rdzeń ma całą geometrię), trudniejsze
  są czcionki: trzeba osadzić podzbiór Segoe UI (TrueType/CID) z polskimi znakami; emoji mogą zostać obrazkami.
- **Druk: podgląd wszystkich stron i znaczniki do sklejania [S]:** przy podziale na strony — miniatury całego plakatu
  i delikatne znaczniki zakładki/cięcia na krawędziach.
- **Historia wersji [M]:** autozapis już działa — można by trzymać kilka kopii z ostatnich dni („przywróć mapę
  z wczoraj”).
- **Import z FreeMind/Freeplane (.mm) [M]:** to prosty XML — kolejny popularny format map.
- **Miniatury `.mapo` w Eksploratorze [L]:** shell extension rysujący podgląd (wymaga osobnej biblioteki COM).

## 🧑‍🤝‍🧑 Wygoda
- **Tryb prezentacji [M]:** pełny ekran, przechodzenie po gałęziach strzałkami, odsłanianie kolejnych kafelków
  (np. prowadzenie sesji RPG albo prezentacja pomysłu) — fokus na gałęzi i płynne przejścia widoku już są.
- **Ostatnie mapy na ekranie startowym [S]:** miniatury zamiast pustej mapy przy uruchomieniu bez pliku.
- **Wersja angielska [M]:** teksty interfejsu w zasobach `.resw` zamiast w kodzie.
- **Obsługa dotyku i pióra [M]:** szczypanie do powiększania, przesuwanie dwoma palcami, długie przytrzymanie = menu.

## 🛠️ Technika
- **Więcej testów interfejsu [S]:** podstawowe już są (FlaUI) — brakuje obrazków (schowek, upuszczanie pliku),
  motywów, menu kontekstowych i okien dialogowych (druk, eksport).
- Na później (dziś program jest za mały, żeby się tym przejmować): podpisywanie exe certyfikatem, aktualizacje
  różnicowe.
