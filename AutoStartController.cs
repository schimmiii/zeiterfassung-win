namespace Zeiterfassung;

/// <summary>
/// Kantengesteuertes Auto-Start per WLAN. Regeln 1:1 aus der Swift-App:
///   zuhause/buero  → Start
///   auswaerts      → Stop (nur wenn StopWhenAway aktiv)
///   kein WLAN      → Stop
///   nicht ermittelbar → NICHT eingreifen
///
/// Kantensteuerung: es wird nur bei WLAN-*Wechsel* gehandelt, nicht bei jedem Poll.
/// Damit stoppt die App nicht sofort wieder, wenn der Nutzer auswaerts manuell startet.
/// </summary>
public sealed class AutoStartController
{
    private readonly TimeTracker _tracker;
    private readonly Settings _settings;
    private string? _lastKey;

    public AutoStartController(TimeTracker tracker, Settings settings)
    {
        _tracker = tracker;
        _settings = settings;
    }

    /// <summary>Erzwingt Neubewertung beim naechsten <see cref="Evaluate"/> (z.B. nach Settings-Aenderung).</summary>
    public void Reset() => _lastKey = null;

    public void Evaluate(WifiReading reading)
    {
        if (!_settings.AutoStartEnabled) return;
        if (reading.Key == _lastKey) return; // Kante: nur bei Wechsel handeln
        _lastKey = reading.Key;

        switch (reading.State)
        {
            case WifiState.Unavailable:
                return; // SSID nicht ermittelbar → nicht eingreifen

            case WifiState.Disconnected:
                _tracker.Stop();
                return;

            case WifiState.Connected:
                if (Matches(reading.Ssid, _settings.HomeSsid) ||
                    Matches(reading.Ssid, _settings.OfficeSsid))
                {
                    _tracker.Start();
                }
                else if (_settings.StopWhenAway)
                {
                    _tracker.Stop();
                }
                return;
        }
    }

    private static bool Matches(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) &&
        !string.IsNullOrWhiteSpace(b) &&
        string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}
