# SETUP — Bauen, Testen, Rückmelden

Hi Sebastian J.,

Zeiterfassungs-Tray-App, jetzt **funktional vollständig**. Baubar mit Claude Code + .NET 8.
Ich konnte auf meiner Build-Umgebung nur kompilieren, nicht ausführen — daher brauche
ich einen Testlauf auf echtem Windows. Danke schon mal!

## 1. Voraussetzung
.NET 8 SDK:  `dotnet --version`  (sonst: https://dotnet.microsoft.com/download/dotnet/8.0)

## 2. Starten (Test)
    cd zeiterfassung-win
    dotnet run -c Release
Konsolenfenster offen lassen (zeigt Fehler). Tray-Icon erscheint unten rechts (evtl. unter „^").

## 3. Eigenständige .exe (echte Nutzung)
    dotnet publish -c Release -r win-x64
→ `bin/Release/net8.0-windows/win-x64/publish/Zeiterfassung.exe` (doppelklickbar).

## 4. Testablauf (Screenshots wo möglich)

**Tray-Icon**
1. Icon da? Zeigt „0 / 00" (Stunden über Minuten). Auf deiner Skalierung lesbar? (Screenshot)

**Popup (Linksklick)**
2. Öffnet unten rechts, schließt bei Klick daneben (Fokusverlust)?
3. Icons in den Zeilen korrekt? (Segoe-Fluent-Glyphen — bei leeren Kästchen bitte Screenshot,
   dann stimmt ein Codepoint nicht; zentral in `Glyphs` justierbar.)
4. Start/Stop: Zahl tickt hoch/stoppt, „seit HH:MM" plausibel.
5. STATUS: echter WLAN-Name in der Kopfzeile? „merken" ordnet aktuelles Netz zu.
6. Saldo-Zeile plausibel (grün/rot, +/−)?
7. Woche: Balken + Summe stimmig?

**Übersicht / Export**
8. Übersicht: Monat/Jahr, mit ‹ › blättern (nicht in die Zukunft). Balken + Summe.
9. Export: Scope + CSV/XLSX + „Detailliert" → speichern.
   - XLSX in Excel: Dauer als Zahl mit Komma (z. B. 12,27), Umlaute korrekt?
   - CSV in Excel: Semikolon, Datum TT.MM.JJJJ, Summenzeile?
   (XLSX-Struktur ist bereits gegen einen Excel-Parser validiert.)

**Nachtragen**
10. Datum/Von/Bis → „Hinzufügen" → erscheint unter HEUTE; löschbar; laufendes zeigt „läuft".

**Einstellungen**
11. Toggles (Auto-Start / Auswärts stoppen / Beim Login starten) schalten sichtbar um?
    „Auswärts stoppen" ist ausgegraut, wenn Auto-Start aus ist.
12. Zeitkonto: Wochenstunden/Anfangssaldo (Zahlenfelder), Kontostart (Datum) → wirkt auf Saldo?
13. WLAN-Zuordnung: „×" löscht die Zuordnung.
14. **Beim Login starten** an → Windows neu anmelden → startet die App automatisch?
    (Prüfbar auch in: Task-Manager → Autostart → „Zeiterfassung".)

**WLAN-Auto-Start** (kritisch, plattformabhängig)
15. Auto-Start ist Default AN. WLAN trennen → stoppt? Bekanntes Netz verbinden → startet?
16. Falls „nicht ermittelbar (Standort-Freigabe?)": Windows-Standortdienste an
    (Einstellungen → Datenschutz → Standort) → wird SSID danach lesbar?

**Sleep/Lock**
17. Zeit läuft, dann Bildschirm sperren (Win+L) → beim Entsperren: hat die Zeit während
    der Sperre pausiert (kein Sprung)? Gleiches bei Standby/Aufwecken.

## 5. Rückmeldung an Sebby
Wichtigste Punkte: 3 (Icons), 6 (Saldo), 9 (Export-Anzeige), 14 (Login), 15/16 (WLAN), 17 (Sleep/Lock).
Bei Absturz: Text aus dem `dotnet run`-Fenster. Daten: `%APPDATA%\Zeiterfassung\`.

Danke!
