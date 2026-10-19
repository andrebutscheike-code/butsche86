# Seitenwerk

Layoutprogramm für Windows im Stil von Microsoft Publisher: Seiten mit frei platzierbaren Textfeldern, Bildern, Formen und Linien, Menüband, Seitennavigation, Lineale. Öffnet Publisher-Dateien (`.pub`).

## Starten

GitHub baut bei jeder Änderung unter `Seitenwerk/` automatisch eine fertige EXE:
**Actions** → **Seitenwerk bauen** → neuester erfolgreicher Lauf → unter **Artifacts** `Seitenwerk` herunterladen, entpacken, doppelklicken. Ein .NET muss nicht installiert sein (die EXE bringt es mit, rund 100 MB). Beim ersten Start warnt Windows SmartScreen möglicherweise („Weitere Informationen“ → „Trotzdem ausführen“), weil die Datei nicht signiert ist.

Im selben Lauf steht unter **Seitenwerk-Selbsttest** ein Protokoll samt Bildschirmfotos des Fensters.

## Publisher-Dateien (.pub) öffnen

Das Publisher-Format ist nicht öffentlich verständlich dokumentiert. Seitenwerk lässt die Datei deshalb von **LibreOffice** (Import-Filter `libmspub`) im Hintergrund in eine Zeichnung umwandeln und liest daraus Textfelder, Bilder, Formen, Linien und Tabellen als einzeln bearbeitbare Objekte ein.

- LibreOffice muss installiert sein ([Download](https://de.libreoffice.org/download/download-libreoffice/)). Es wird nicht geöffnet, nur im Hintergrund benutzt. Findet Seitenwerk es nicht, fragt das Programm nach. Unter **Datei → LibreOffice für .pub-Dateien festlegen** lässt sich der Ort wählen.
- Das Ergebnis kann vom Original abweichen. Es hängt davon ab, was LibreOffice aus der Datei macht. Bekannte Unterschiede: Schriftarten, die auf dem Computer fehlen, werden ersetzt; Farbverläufe werden einfarbig; Schatten und Effekte fehlen; Tabellen werden zu einer Gruppe aus Textfeldern (eine Zelle = ein Textfeld); Text, der nicht in einen Rahmen passt, wird abgeschnitten (rote Markierung am Rahmen), und der Textfluss in verbundene Rahmen wird nicht übernommen.
- Grafiken im Format SVM oder SVG werden mit Hinweis ausgelassen. WMF/EMF-Cliparts werden als Bild übernommen.
- Eine `.pub`-Datei lässt sich nicht zurückschreiben. Speichern Sie importierte Dateien als `.swk` (Seitenwerk-Format); Export als PDF oder PNG ist möglich.
- Passwortgeschützte oder beschädigte Dateien meldet das Programm als Fehler.

Getestet wurde mit echten Publisher-Dateien aus den Testdaten von Apache POI (Apache License 2.0; siehe `Seitenwerk/tests/Seitenwerk.Tests/Testdaten/NOTICE.txt`), darunter eine Broschüre mit Cliparts, Fotos, Gruppen und mehreren Schriftarten sowie ein Dokument mit Tabelle. Andere Dateien können sich anders verhalten.

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

Echte Tabellen (importierte werden zu Textfeldern), Textfluss zwischen verbundenen Textfeldern, Spalten in Textfeldern, Musterseiten, Seitenzahl-Felder, WordArt, Farbverläufe und Schatten, Seriendruck, Zurückschreiben nach `.pub`. PDF-Export legt jede Seite als hochauflösendes Bild (300 dpi) in das PDF; der Text ist darin nicht auswählbar. Für ein PDF mit auswählbarem Text beim Drucken den Drucker „Microsoft Print to PDF“ wählen.

## Entwicklung

```
dotnet test Seitenwerk/tests/Seitenwerk.Tests       # Modell, Speichern, Import, PDF (läuft auch unter Linux; mit installiertem LibreOffice auch das Öffnen echter .pub-Dateien)
dotnet run --project Seitenwerk/src/Seitenwerk      # nur unter Windows
Seitenwerk.exe --selbsttest ORDNER                  # Fenster aufbauen, rendern, Befehle prüfen, Protokoll und Bilder in ORDNER
                                                    # (mit SEITENWERK_TEST_PUBS=Ordner werden dessen .pub-Dateien zusätzlich geöffnet)
```

- `src/Seitenwerk.Core` (plattformunabhängig): Dokumentmodell, Dateiformat `.swk` (ZIP mit `dokument.json` und `medien/`), ODG-/PUB-Import, Rückgängig-Verlauf, Ausrichten/Gruppieren, PDF-Schreiber.
- `src/Seitenwerk` (WPF, .NET 10): Menüband, Seitenansicht, Zeichnen, Textbearbeitung, Export, Druck.
- `tests/Seitenwerk.Tests`: Modultests; `Testdaten/lo-zeichnung.odg` ist eine mit LibreOffice erzeugte Zeichnung.

## Stand der Prüfung

Auf einem Windows-Rechner (GitHub Actions) automatisch geprüft: Fenster und Menüband laden, alle Menübefehle sind belegt, Seiten rendern, Speichern/Laden, Rückgängig/Wiederholen, Gruppieren, Ausrichten, Seitenverwaltung, Textbearbeitung im Feld, PDF/PNG-Export, Druckseiten, Zwischenablage und das Öffnen echter `.pub`-Dateien mit allen Bildern.

Nicht automatisch geprüft ist die Bedienung mit der Maus im laufenden Fenster (Ziehen, Skalieren, Drehen, Einrasten, Zeichnen neuer Objekte), das Aussehen des Menübands im Einzelnen und der Druck auf einem echten Drucker.
