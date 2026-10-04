# Publisher nach Word

Wandelt Microsoft-Publisher-Dateien (`.pub`) in Word-Dateien (`.docx`) um – unabhängig davon, ob Publisher noch installiert ist.

Ablauf je Datei: `.pub` → PDF (LibreOffice im Hintergrund) → `.docx`.

## Variante ohne Python: fertige EXE (Windows)

GitHub baut bei jeder Änderung an `pub2docx.py` automatisch eine `Publisher-nach-Word.exe`:
Im Repository auf **Actions** → **Windows-EXE bauen** → neuester erfolgreicher Lauf → unten unter **Artifacts** `Publisher-nach-Word` herunterladen, entpacken, doppelklicken. Python ist dafür nicht nötig, **LibreOffice schon**. Die Datei ist rund 100 MB groß. Beim ersten Start warnt Windows Defender SmartScreen möglicherweise („Weitere Informationen“ → „Trotzdem ausführen“), weil die Datei nicht signiert ist.

Die EXE hat nur die grafische Oberfläche. Für die Kommandozeile `pub2docx.py` mit Python verwenden.

## Voraussetzungen (nur für `pub2docx.py`)

1. [Python](https://www.python.org/downloads/) ab Version 3.9. Bei der Installation „Add python.exe to PATH“ anhaken.
2. [LibreOffice](https://www.libreoffice.org/download/) (kostenlos, wird auch für die EXE benötigt). Es muss nur installiert sein, nicht geöffnet.

## Installation (Windows)

`installieren.bat` doppelklicken. Das installiert die benötigte Python-Bibliothek (`pdf2docx`).

Auf anderen Systemen: `pip install -r requirements.txt`.

## Benutzung

**Grafisch:** `starten.bat` doppelklicken, Dateien oder einen ganzen Ordner hinzufügen, Zielordner und Art der Umwandlung wählen, auf „Umwandeln“ klicken.

**Kommandozeile:**

```
python pub2docx.py Flyer.pub
python pub2docx.py Ordner_mit_pubs -o Word_Dateien
python pub2docx.py Flyer.pub --modus bild
```

Ordner werden samt Unterordnern nach `.pub`-Dateien durchsucht. Bereits vorhandene `.docx`-Dateien werden nie überschrieben, stattdessen heißt die neue Datei `Name (2).docx`.

## Zwei Arten der Umwandlung

| Modus | Ergebnis |
|---|---|
| `bearbeitbar` (Standard) | Texte und Bilder bleiben in Word einzeln bearbeitbar. Das Layout wird angenähert, bei komplexen Seiten verschieben sich Elemente. |
| `bild` | Jede Seite wird als Bild in Word eingefügt. Sieht exakt aus wie das Original, Text ist aber nicht bearbeitbar. |

## Grenzen

- Publisher ist ein Layoutprogramm (freie Textfelder), Word ein Textverarbeitungsprogramm. Eine vollständig identische, zugleich frei bearbeitbare Umsetzung gibt es nicht. Ergebnisse immer prüfen.
- Den Publisher-Import übernimmt LibreOffice. Wie gut er mit sehr neuen Publisher-Dateien zurechtkommt, hängt von der LibreOffice-Version ab. Eine aktuelle Version verwenden.
- Fehlende Schriftarten ersetzt LibreOffice durch ähnliche, dadurch ändern sich Zeilenumbrüche.
- Passwortgeschützte oder beschädigte Dateien werden mit einer Fehlermeldung übersprungen, die übrigen Dateien werden trotzdem umgewandelt.
