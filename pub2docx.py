#!/usr/bin/env python3
"""Microsoft-Publisher-Dateien (.pub) in Word-Dateien (.docx) umwandeln.

Ablauf je Datei:
  1. .pub -> PDF   (LibreOffice im Hintergrund, Import-Filter "libmspub")
  2. PDF  -> .docx (pdf2docx bzw. Seitenbilder)

Ohne Argumente startet die grafische Oberfläche, mit Argumenten die Kommandozeile:
  python pub2docx.py Flyer.pub Ordner_mit_pubs -o Ausgabe --modus bearbeitbar
"""
import argparse
import os
import queue
import shutil
import subprocess
import sys
import tempfile
import threading
from pathlib import Path

MODI = {
    "bearbeitbar": "Bearbeitbar (Text und Bilder bleiben einzeln editierbar, Layout wird angenähert)",
    "bild": "Seitenbild (jede Seite als Bild, sieht exakt aus wie das Original, nicht editierbar)",
}
LIBREOFFICE_TIMEOUT = 300  # Sekunden pro Datei


class KonvertierungsFehler(Exception):
    pass


def finde_soffice():
    for name in ("soffice", "libreoffice"):
        pfad = shutil.which(name)
        if pfad:
            return pfad
    kandidaten = [
        os.path.join(os.environ.get("ProgramFiles", r"C:\Program Files"), "LibreOffice", "program", "soffice.exe"),
        os.path.join(os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)"), "LibreOffice", "program", "soffice.exe"),
        "/Applications/LibreOffice.app/Contents/MacOS/soffice",
    ]
    for kandidat in kandidaten:
        if os.path.exists(kandidat):
            return kandidat
    return None


def sammle_pub_dateien(pfade):
    """Dateien übernehmen, Ordner nach .pub-Dateien durchsuchen."""
    gefunden = []
    for eintrag in pfade:
        p = Path(eintrag)
        if p.is_dir():
            gefunden.extend(sorted(x for x in p.rglob("*") if x.suffix.lower() == ".pub"))
        elif p.is_file():
            gefunden.append(p)
        else:
            raise KonvertierungsFehler(f"Nicht gefunden: {p}")
    return gefunden


def freier_name(pfad):
    """Vorhandene Dateien nie überschreiben: 'Flyer.docx' -> 'Flyer (2).docx'."""
    pfad = Path(pfad)
    kandidat, n = pfad, 2
    while kandidat.exists():
        kandidat = pfad.with_name(f"{pfad.stem} ({n}){pfad.suffix}")
        n += 1
    return kandidat


def pub_zu_pdf(soffice, pub, arbeitsordner, profil):
    ausgabe = Path(arbeitsordner)
    cmd = [
        soffice,
        f"-env:UserInstallation={Path(profil).as_uri()}",  # eigenes Profil: stört keine offene LibreOffice-Sitzung
        "--headless", "--norestore", "--nolockcheck",
    ]
    if Path(pub).suffix.lower() == ".pub":
        # Filter erzwingen, sonst öffnet LibreOffice fehlerhafte .pub-Dateien stillschweigend als Text
        cmd.append("--infilter=Publisher Document")
    cmd += ["--convert-to", "pdf:draw_pdf_Export", "--outdir", str(ausgabe), str(pub)]
    flags = subprocess.CREATE_NO_WINDOW if sys.platform == "win32" else 0
    try:
        ergebnis = subprocess.run(cmd, capture_output=True, text=True, timeout=LIBREOFFICE_TIMEOUT, creationflags=flags)
    except subprocess.TimeoutExpired:
        raise KonvertierungsFehler(f"LibreOffice hat nach {LIBREOFFICE_TIMEOUT} s nicht geantwortet.")
    pdf = ausgabe / (Path(pub).stem + ".pdf")
    if not pdf.exists():
        zeilen = (ergebnis.stderr or ergebnis.stdout or "").splitlines()
        details = "\n".join(z for z in zeilen if "javaldx" not in z).strip()
        raise KonvertierungsFehler(
            "LibreOffice konnte die Datei nicht öffnen (beschädigt, passwortgeschützt oder kein gültiges Publisher-Format)."
            + (f"\n{details}" if details else "")
        )
    return pdf


def pdf_zu_docx_bearbeitbar(pdf, docx):
    import logging

    from pdf2docx import Converter

    logging.getLogger().setLevel(logging.WARNING)
    konverter = Converter(str(pdf))
    try:
        konverter.convert(str(docx))
    finally:
        konverter.close()


def pdf_zu_docx_bild(pdf, docx, arbeitsordner, dpi=200):
    try:
        import pymupdf
    except ImportError:  # ältere PyMuPDF-Versionen
        import fitz as pymupdf
    from docx import Document
    from docx.enum.section import WD_SECTION
    from docx.shared import Pt

    dokument = Document()
    normal = dokument.styles["Normal"]
    normal.font.size = Pt(1)  # Absatzmarke und Abschnittswechsel sollen kaum Platz brauchen
    normal.paragraph_format.space_before = Pt(0)
    normal.paragraph_format.space_after = Pt(0)
    normal.paragraph_format.line_spacing = 1

    rand = 5  # Punkt
    with pymupdf.open(str(pdf)) as seiten:
        for nr, seite in enumerate(seiten):
            breite, hoehe = seite.rect.width, seite.rect.height
            abschnitt = dokument.sections[0] if nr == 0 else dokument.add_section(WD_SECTION.NEW_PAGE)
            abschnitt.page_width, abschnitt.page_height = Pt(breite), Pt(hoehe)
            abschnitt.left_margin = abschnitt.right_margin = Pt(rand)
            abschnitt.top_margin = abschnitt.bottom_margin = Pt(rand)
            abschnitt.header_distance = abschnitt.footer_distance = Pt(0)

            bild = Path(arbeitsordner) / f"seite_{nr + 1}.png"
            seite.get_pixmap(dpi=dpi).save(str(bild))
            faktor = min((breite - 2 * rand) / breite, (hoehe - 2 * rand - 4) / hoehe)
            dokument.add_picture(str(bild), width=Pt(breite * faktor))
    dokument.save(str(docx))


def konvertiere(pfade, ausgabeordner=None, modus="bearbeitbar", log=print):
    """Wandelt die angegebenen Dateien/Ordner um. Gibt (erfolge, fehler) zurück."""
    if modus not in MODI:
        raise KonvertierungsFehler(f"Unbekannter Modus: {modus}")
    soffice = finde_soffice()
    if not soffice:
        raise KonvertierungsFehler(
            "LibreOffice wurde nicht gefunden. Bitte von https://www.libreoffice.org installieren."
        )
    dateien = sammle_pub_dateien(pfade)
    if not dateien:
        raise KonvertierungsFehler("Keine .pub-Dateien gefunden.")

    erfolge, fehler = [], []
    with tempfile.TemporaryDirectory(prefix="pub2docx_") as temp:
        profil = Path(temp) / "profil"
        for i, pub in enumerate(dateien, 1):
            log(f"[{i}/{len(dateien)}] {pub.name} ...")
            try:
                arbeit = Path(temp) / f"datei_{i}"
                arbeit.mkdir()
                pdf = pub_zu_pdf(soffice, pub, arbeit, profil)
                zielordner = Path(ausgabeordner) if ausgabeordner else pub.parent
                zielordner.mkdir(parents=True, exist_ok=True)
                ziel = freier_name(zielordner / (pub.stem + ".docx"))
                if modus == "bild":
                    pdf_zu_docx_bild(pdf, ziel, arbeit)
                else:
                    pdf_zu_docx_bearbeitbar(pdf, ziel)
                log(f"    gespeichert: {ziel}")
                erfolge.append(ziel)
            except Exception as e:  # eine fehlerhafte Datei soll die übrigen nicht stoppen
                log(f"    FEHLER: {e}")
                fehler.append((pub, str(e)))
    log(f"Fertig: {len(erfolge)} umgewandelt, {len(fehler)} fehlgeschlagen.")
    return erfolge, fehler


def starte_gui():
    import tkinter as tk
    from tkinter import filedialog, messagebox, scrolledtext

    fenster = tk.Tk()
    fenster.title("Publisher nach Word")
    fenster.minsize(640, 520)

    dateien = []
    ausgabe = tk.StringVar(value="")
    modus = tk.StringVar(value="bearbeitbar")
    meldungen = queue.Queue()

    tk.Label(fenster, text="Publisher-Dateien (.pub):").pack(anchor="w", padx=10, pady=(10, 0))
    liste = tk.Listbox(fenster, height=8, selectmode=tk.EXTENDED)
    liste.pack(fill="both", expand=True, padx=10)

    def hinzufuegen(pfade):
        for p in pfade:
            if p not in dateien:
                dateien.append(p)
                liste.insert(tk.END, p)

    def dateien_waehlen():
        hinzufuegen(filedialog.askopenfilenames(title="Publisher-Dateien wählen", filetypes=[("Publisher", "*.pub"), ("Alle Dateien", "*.*")]))

    def ordner_waehlen():
        ordner = filedialog.askdirectory(title="Ordner mit Publisher-Dateien wählen")
        if ordner:
            hinzufuegen([str(p) for p in sammle_pub_dateien([ordner])])

    def entfernen():
        for index in reversed(liste.curselection()):
            liste.delete(index)
            del dateien[index]

    knoepfe = tk.Frame(fenster)
    knoepfe.pack(fill="x", padx=10, pady=5)
    tk.Button(knoepfe, text="Dateien hinzufügen …", command=dateien_waehlen).pack(side="left")
    tk.Button(knoepfe, text="Ordner hinzufügen …", command=ordner_waehlen).pack(side="left", padx=5)
    tk.Button(knoepfe, text="Auswahl entfernen", command=entfernen).pack(side="left")

    tk.Label(fenster, text="Speichern in:").pack(anchor="w", padx=10)
    zeile = tk.Frame(fenster)
    zeile.pack(fill="x", padx=10)
    tk.Entry(zeile, textvariable=ausgabe).pack(side="left", fill="x", expand=True)

    def ausgabe_waehlen():
        ordner = filedialog.askdirectory(title="Zielordner wählen")
        if ordner:
            ausgabe.set(ordner)

    tk.Button(zeile, text="Wählen …", command=ausgabe_waehlen).pack(side="left", padx=(5, 0))
    tk.Label(fenster, text="(leer = neben der Original-Datei)", fg="gray").pack(anchor="w", padx=10)

    tk.Label(fenster, text="Art der Umwandlung:").pack(anchor="w", padx=10, pady=(8, 0))
    for schluessel, text in MODI.items():
        tk.Radiobutton(fenster, text=text, variable=modus, value=schluessel, wraplength=600, justify="left").pack(anchor="w", padx=20)

    protokoll = scrolledtext.ScrolledText(fenster, height=8, state="disabled")
    start = tk.Button(fenster, text="Umwandeln")

    def schreibe(text):
        protokoll.configure(state="normal")
        protokoll.insert(tk.END, text + "\n")
        protokoll.see(tk.END)
        protokoll.configure(state="disabled")

    def hole_meldungen():
        try:
            while True:
                eintrag = meldungen.get_nowait()
                if eintrag is None:
                    start.configure(state="normal")
                    return
                schreibe(eintrag)
        except queue.Empty:
            pass
        fenster.after(100, hole_meldungen)

    def arbeite(pfade, ziel, art):
        try:
            konvertiere(pfade, ziel or None, art, log=meldungen.put)
        except KonvertierungsFehler as e:
            meldungen.put(f"FEHLER: {e}")
        except Exception as e:
            meldungen.put(f"Unerwarteter Fehler: {e}")
        meldungen.put(None)

    def umwandeln():
        if not dateien:
            messagebox.showinfo("Hinweis", "Bitte zuerst Publisher-Dateien auswählen.")
            return
        if not finde_soffice():
            messagebox.showerror("LibreOffice fehlt", "LibreOffice wurde nicht gefunden.\nBitte von https://www.libreoffice.org installieren.")
            return
        start.configure(state="disabled")
        threading.Thread(target=arbeite, args=(list(dateien), ausgabe.get().strip(), modus.get()), daemon=True).start()
        hole_meldungen()

    start.configure(command=umwandeln)
    start.pack(pady=8)
    protokoll.pack(fill="both", expand=True, padx=10, pady=(0, 10))
    fenster.mainloop()


def main():
    parser = argparse.ArgumentParser(description="Publisher-Dateien (.pub) in Word (.docx) umwandeln.")
    parser.add_argument("pfade", nargs="*", help=".pub-Dateien oder Ordner (ohne Angabe startet die grafische Oberfläche)")
    parser.add_argument("-o", "--ausgabe", help="Zielordner (Standard: neben der Original-Datei)")
    parser.add_argument("-m", "--modus", choices=list(MODI), default="bearbeitbar", help="bearbeitbar (Standard) oder bild")
    argumente = parser.parse_args()

    if not argumente.pfade:
        starte_gui()
        return 0
    try:
        _, fehler = konvertiere(argumente.pfade, argumente.ausgabe, argumente.modus)
    except KonvertierungsFehler as e:
        print(f"FEHLER: {e}", file=sys.stderr)
        return 2
    return 1 if fehler else 0


if __name__ == "__main__":
    sys.exit(main())
