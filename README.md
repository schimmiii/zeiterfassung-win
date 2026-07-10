# Zeiterfassung (Windows)

Tray-App zur Arbeitszeiterfassung. Windows-Neubau der macOS-Menüleisten-App —
gleiche Regeln, gleiches Segment-Modell, gleicher Funktionsstand.

**Status: funktional vollständig (Push 4 von 4).**

Funktionen:
- Zeit tracken (Start/Stop, Segmente, Persistenz, Sekundentakt)
- Tray-Icon mit zweizeiliger Tagessumme (laufend blau / gestoppt grau)
- WLAN-Auto-Start (kantengesteuert: zuhause/büro→Start, auswärts→Stop, kein WLAN→Stop)
- Popup-Panel (Linksklick): Home (STATUS + Saldo), Woche, Übersicht (Monat/Jahr),
  Export (CSV/XLSX), Nachtragen, Einstellungen
- Zeitkonto (Modell B): Saldo grün/rot, Wochenstunden/Anfangssaldo/Kontostart
- Auto-Start bei Login (Registry Run-Key)
- Pause bei Sleep/Bildschirmsperre, weiter bei Wake/Unlock

Bedienung: Linksklick = Popup, Rechtsklick = Start/Stop + Beenden.

Siehe **SETUP.md** zum Bauen, Testen und für die Rückmeldung.

## Was auf echtem Windows noch verifiziert werden muss
Alles kompiliert sauber; die XLSX-Erzeugung ist gegen einen Excel-Parser validiert.
NICHT laufzeit-geprüft (Linux-Build-Umgebung): WLAN-SSID-Lesen (P/Invoke-Offsets),
Win11-Standort-Gate, Icon-Glyphen (Segoe Fluent Codepoints), Sleep/Lock-Events,
Login-Autostart. Details in SETUP.md.
