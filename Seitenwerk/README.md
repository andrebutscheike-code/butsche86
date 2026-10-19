# Seitenwerk

Layoutprogramm für Windows im Stil von Microsoft Publisher: Seiten mit frei platzierbaren Textfeldern, Bildern, Formen und Linien, Menüband, Seitennavigation, Lineale. Öffnet Publisher-Dateien (`.pub`).

## Starten

GitHub baut bei jeder Änderung unter `Seitenwerk/` automatisch eine fertige EXE:
**Actions** → **Seitenwerk bauen** → neuester erfolgreicher Lauf → unter **Artifacts** `Seitenwerk` herunterladen, entpacken, doppelklicken. Ein .NET muss nicht installiert sein (die EXE bringt es mit, rund 100 MB). Beim ersten Start warnt Windows SmartScreen möglicherweise („Weitere Informationen“ → „Trotzdem ausführen“), weil die Datei nicht signiert ist.

Im selben Lauf steht unter **Seitenwerk-Selbsttest** ein Protokoll samt Bildschirmfotos des Fensters.

## Publisher-Dateien (.pub) öffnen

Das Publisher-Format ist nicht öffentlich verständlich dokumentiert. Seitenwerk lässt die Datei deshalb von **LibreOffice** (Import-Filter `libmspub`) im Hintergrund in eine Zeichnung umwandeln und liest Textfelder, Bilder, Formen und Linien als einzeln bearbeitbare Objekte ein.

- LibreOffice muss installiert sein ([Download](https://de.libreoffice.org/download/download-libreoffice/)). Es wird nicht geöffnet, nur im Hintergrund benutzt. Findet Seitenwerk es nicht, fragt das Programm nach.
- Das Ergebnis kann vom Original abweichen: Schriftarten, Abstände, Farbverläufe (werden einfarbig), Bildfüllungen, Schatten und Effekte. Metadateien (WMF/EMF) werden als Bild übernommen, StarView-Metadateien (SVM) nicht.
- Eine `.pub`-Datei lässt sich nicht zurückschreiben. Speichern Sie importierte Dateien als `.swk` (Seitenwerk-Format), Export als PDF oder PNG ist möglich.
- Passwortgeschützte oder beschädigte Dateien meldet das Programm als Fehler.

Außerdem lassen sich OpenDocument-Zeichnungen (`.odg`) öffnen.

## Funktionen

| Bereich | Umfang |
|---|---|
| Datei | Neu (Papierformat, Ränder), Öffnen, Speichern (`.swk`), Zuletzt geöffnet, PDF-Export, Seiten als PNG, Drucken |
| Seiten | Einfügen, Duplizieren, Löschen, Verschieben, Seitennavigation mit Vorschau, Hoch-/Querformat, Ränder, Hintergrundfarbe |
| Objekte | Textfeld, Bild (PNG, JPG, GIF, BMP, TIF, EMF, WMF), Formen (Rechteck, abgerundetes Rechteck, Ellipse, Dreieck, Raute, Fünfeck, Sechseck, Pfeil, Stern), Linie, Pfeil, Freiformpfade aus Importen |
| Text | Schriftart, -größe, fett, kursiv, unterstrichen, durchgestrichen, Farbe, Ausrichtung (links, zentriert, rechts, Blocksatz), Zeilenabstand, vertikale Ausrichtung, Rechtschreibprüfung |
| Bearbeiten | Rückgängig/Wiederholen, Ausschneiden/Kopieren/Einfügen (auch Bilder und Text aus der Zwischenablage), Duplizieren, Drag & Drop von Bildern |
| Anordnen | Vordergrund/Hintergrund, Ebenen, Ausrichten, Verteilen, Gruppieren, Drehen (Griff oder 90°), Sperren, Fangen an Rändern, Seitenmitte und anderen Objekten, Raster |
| Ansicht | Zoom (Strg + Mausrad), Lineale in cm, Ränder und Raster einblenden |

Maus: Ziehen verschiebt, Eckgriffe skalieren (Umschalt hält das Seitenverhältnis bei Formen, Bilder behalten es von selbst), runder Griff dreht (Umschalt rastet in 15°-Schritten ein). Doppelklick auf ein Textfeld oder eine Form bearbeitet den Text. Pfeiltasten verschieben um 1 mm (Strg: 0,1 mm, Umschalt: 10 mm). Esc beendet die Texteingabe.

## Nicht enthalten

Tabellen, Textfluss zwischen verbundenen Textfeldern, Spalten in Textfeldern, Musterseiten, Seitenzahl-Felder, WordArt, Farbverläufe und Schatten, Seriendruck, Zurückschreiben nach `.pub`. PDF-Export legt jede Seite als hochauflösendes Bild (300 dpi) in das PDF; der Text ist darin nicht auswählbar. Für ein PDF mit auswählbarem Text beim Drucken den Drucker „Microsoft Print to PDF“ wählen.

## Entwicklung

```
dotnet test Seitenwerk/tests/Seitenwerk.Tests       # Modell, Speichern, Import, PDF (läuft auch unter Linux)
dotnet run --project Seitenwerk/src/Seitenwerk      # nur unter Windows
Seitenwerk.exe --selbsttest ORDNER                  # Fenster aufbauen, rendern, Befehle prüfen, Protokoll und Bilder in ORDNER
```

- `src/Seitenwerk.Core` (plattformunabhängig): Dokumentmodell, Dateiformat `.swk` (ZIP mit `dokument.json` und `medien/`), ODG-/PUB-Import, Rückgängig-Verlauf, Ausrichten/Gruppieren, PDF-Schreiber.
- `src/Seitenwerk` (WPF, .NET 10): Menüband, Seitenansicht, Zeichnen, Textbearbeitung, Export, Druck.
- `tests/Seitenwerk.Tests`: Modultests; `Testdaten/lo-zeichnung.odg` ist eine mit LibreOffice erzeugte Zeichnung.
