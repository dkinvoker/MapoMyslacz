# MapoMyślacz — pomysły na rozwój

Lista rzeczy, które jeszcze nie są zrobione. Gotowe funkcje opisuje [CHANGELOG.md](CHANGELOG.md).
Orientacyjna ilość pracy: **[S]** mała, **[M]** średnia, **[L]** duża.

## ⭐ Następne w kolejce
- **Tryb prezentacji [M]:** pełny ekran, przechodzenie po gałęziach strzałkami, odsłanianie kolejnych kafelków
  (np. prowadzenie sesji RPG albo prezentacja pomysłu) — fokus na gałęzi i płynne przejścia widoku już są.
- **Szukanie we wszystkich mapach folderu [M]:** wyszukiwarka przeszukuje już wszystkie arkusze pliku — tryb
  „kampania” przeszukiwałby też inne mapy z folderu (linki zwrotne już przeglądają folder w tle).
- **Obrazki — dalsze kroki [S–M]:** opcja „trzymaj obok mapy” dla dużych zdjęć (plik w folderze mapy zamiast base64),
  obrazek obok tekstu zamiast nad nim, przycinanie; zmniejszanie dużych obrazków z XMind tak jak wklejanych.

## 🗺️ Kampania (mapy połączone linkami)
- **Podgląd linkowanej mapy [S]:** po najechaniu na znaczek linku — miniatura mapy docelowej (scena Win2D jak
  w minimapie).
- **Eksport kampanii do Obsidiana [S]:** konspekty Markdown wszystkich map z linkami `[[Sesja 2]]` między nimi.

## 🧭 Praca z mapą
- **Ramki zagnieżdżone i zwijane [S]:** ramka w ramce, zwijanie ramki do samego nagłówka (przeciągnięcie kafelka do
  ramki już działa).
- **Układ drzewa w dół [S]:** „Uporządkuj” w wersji pionowej (dzieci pod rodzicem) — z gałęziami łamanymi daje gotowe
  drzewo genealogiczne jednym kliknięciem.

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

## 📄 Pliki i wymiana
- **Wektorowy PDF i SVG [M–L]:** dziś PDF i druk są rastrowe (strony jako obrazy 150–600 dpi — ostre, ale pliki są
  większe, a tekstu nie da się zaznaczyć). Kształty i krzywe przeniesie się łatwo (rdzeń ma całą geometrię), trudniejsze
  są czcionki: trzeba osadzić podzbiór Segoe UI (TrueType/CID) z polskimi znakami; emoji mogą zostać obrazkami.
- **Druk: podgląd wszystkich stron i znaczniki do sklejania [S]:** przy podziale na strony — miniatury całego plakatu
  i delikatne znaczniki zakładki/cięcia na krawędziach. Druk wszystkich arkuszy naraz (PDF już to potrafi; drukarka
  wymaga zmiany orientacji między stronami).
- **Import z FreeMind/Freeplane (.mm) [M]:** to prosty XML — kolejny popularny format map.
- **Miniatury `.mapo` w Eksploratorze [L]:** shell extension rysujący podgląd (wymaga osobnej biblioteki COM).

## 🧑‍🤝‍🧑 Wygoda
- **Ostatnie mapy na ekranie startowym [S]:** miniatury zamiast pustej mapy przy uruchomieniu bez pliku.
- **Wersja angielska [M]:** teksty interfejsu w zasobach `.resw` zamiast w kodzie.
- **Obsługa dotyku i pióra [M]:** szczypanie do powiększania, przesuwanie dwoma palcami, długie przytrzymanie = menu.

## 🛠️ Technika
- **Więcej testów interfejsu [S]:** jest 15 (FlaUI) — brakuje obrazków (schowek, upuszczanie pliku), motywów, menu
  kontekstowych i okien dialogowych (druk, eksport, historia wersji).
- Na później (dziś program jest za mały, żeby się tym przejmować): podpisywanie exe certyfikatem, aktualizacje
  różnicowe.
