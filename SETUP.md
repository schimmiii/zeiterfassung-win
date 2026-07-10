# SETUP — Bauen, Testen, Rueckmelden

Hi Sebastian J.,

das ist eine kleine Tray-App zur Zeiterfassung, aktuell **mitten im Bau** (Push 2 von 4).
Ich brauche von dir einen kurzen **Smoke-Test auf echtem Windows**, weil zwei Dinge
plattformbedingt nicht vorab pruefbar waren (siehe unten). Danach baue ich die
Vollversion (Panel + Auto-Start bei Login) fertig.

Du hast Claude Code — am einfachsten ist, du oeffnest den Ordner damit und laesst
dir beim Bauen/Fixen helfen.

---

## 1. Voraussetzung

.NET 8 SDK. Pruefen:

    dotnet --version

Wenn nichts kommt: https://dotnet.microsoft.com/download/dotnet/8.0 (SDK, nicht nur Runtime).

## 2. Bauen + starten (zum Testen)

    cd zeiterfassung-win
    dotnet run -c Release

Ein Tray-Icon erscheint (unten rechts, evtl. im Ueberlauf-Pfeil "^"). Laeuft ueber
`dotnet run` als Konsolen-Prozess — Fenster offen lassen; Fehler/Abstuerze erscheinen dort.

## 3. Fuer echte Nutzung: eigenstaendige .exe bauen

    dotnet publish -c Release -r win-x64

Ergebnis: `bin/Release/net8.0-windows/win-x64/publish/Zeiterfassung.exe`
Doppelklickbar. (Braucht das .NET-8-Runtime — hast du via SDK.)
Voll autark ohne Runtime-Abhaengigkeit: zusaetzlich `--self-contained true -p:PublishSingleFile=true`
(dann ~150 MB Einzeldatei).

Auto-Start bei Login gibt es noch NICHT (kommt in Push 4) — bis dahin manuell starten.

---

## 4. Was ich getestet brauche (ca. 10 Min)

Bitte kurz durchgehen, Screenshots wo moeglich:

1. **Startet + Tray-Icon da?** Icon zeigt "0 / 00" (Stunden ueber Minuten).
2. **Icon lesbar?** Auf deiner Bildschirm-Skalierung (100/125/150 %) — Screenshot vom
   Tray-Icon. Das ist der Punkt, an dem die zweizeilige Zeit stehen/fallen kann.
3. **Tracking:** Linksklick aufs Icon (oder Rechtsklick -> Starten). Zahl muss hochticken.
   Nochmal klicken -> stoppt.
4. **Persistenz:** Mit laufender Zeit die App beenden (Rechtsklick -> Beenden) und neu
   `dotnet run`. Die Zeit muss weiterlaufen (offenes Segment bleibt offen).
5. **WLAN-SSID (WICHTIG):** Rechtsklick aufs Icon. Oben steht "WLAN: <name>".
   - Steht dort dein echter WLAN-Name? -> gut.
   - Steht dort Muell/leer, obwohl verbunden? -> Byte-Offsets im nativen WLAN-Call
     stimmen nicht. Bitte melden (Screenshot).
   - Steht "nicht ermittelbar (Standort-Freigabe?)" trotz WLAN? -> Windows blockt die
     SSID hinter den Standortdiensten. Test: Einstellungen -> Datenschutz ->
     Standort -> aktivieren, App neu starten. Wird die SSID dann sichtbar? (Genau das
     muss ich wissen.)
6. **Auto-Start-Regel:** Rechtsklick -> "Aktuelles WLAN als Zuhause merken" ->
   "Auto-Start per WLAN" anklicken. Dann WLAN trennen (sollte stoppen) und wieder
   verbinden (sollte starten). Klappt der Wechsel?

## 5. Rueckmeldung an Sebby

- Screenshots von Tray-Icon (versch. Zoomstufen wenn moeglich) + dem Rechtsklick-Menue
- Bei Absturz: den Text aus dem `dotnet run`-Fenster
- Ergebnis von Punkt 5 (SSID) und 6 (Auto-Start) — das sind die kritischen zwei

Daten liegen unter `%APPDATA%\Zeiterfassung\` (segments.json, settings.json) — bei
komischem Verhalten gern mitschicken.

Danke! Sobald 5 + 6 gruen sind, kommt die Version mit Panel und Auto-Start bei Login.
