using System.Windows.Forms;

namespace Zeiterfassung;

/// <summary>
/// Traegt die App ohne Hauptfenster.
/// Push 1: Tray-Icon (zweizeilige Tageszeit), 1s-Update, Start/Stop.
/// Push 2: WLAN-Poll (5s) + Auto-Start-Regeln, temporaere Test-Schalter im Menue.
/// Popup-Panel (Push 3) ersetzt die temporaeren Menue-Eintraege; Sleep/Lock + Autostart (Push 4).
/// </summary>
public sealed class TrayAppContext : ApplicationContext
{
    private readonly TimeTracker _tracker = new();
    private readonly Settings _settings = Settings.Load();
    private readonly AutoStartController _auto;
    private readonly NotifyIcon _notify = new();
    private readonly System.Windows.Forms.Timer _uiTimer = new();
    private readonly System.Windows.Forms.Timer _wifiTimer = new();

    // Menue-Referenzen (Push 2 temporaer)
    private readonly ToolStripMenuItem _toggleItem;
    private readonly ToolStripMenuItem _ssidHeader;
    private readonly ToolStripMenuItem _autoStartItem;
    private readonly ToolStripMenuItem _stopAwayItem;
    private readonly ToolStripMenuItem _setHomeItem;
    private readonly ToolStripMenuItem _setOfficeItem;

    private Icon? _currentIcon;
    private string _lastRendered = "";
    private WifiReading _lastWifi = new(WifiState.Unavailable, null);

    public TrayAppContext()
    {
        _auto = new AutoStartController(_tracker, _settings);

        _ssidHeader = new ToolStripMenuItem("WLAN: —") { Enabled = false };
        _toggleItem = new ToolStripMenuItem("Starten", null, (_, _) => _tracker.Toggle());

        _autoStartItem = new ToolStripMenuItem("Auto-Start per WLAN", null, (_, _) =>
        {
            _settings.AutoStartEnabled = !_settings.AutoStartEnabled;
            _settings.Save();
            _auto.Reset();
            EvaluateWifi();
        }) { CheckOnClick = false };

        _stopAwayItem = new ToolStripMenuItem("Stoppen wenn auswaerts", null, (_, _) =>
        {
            _settings.StopWhenAway = !_settings.StopWhenAway;
            _settings.Save();
            _auto.Reset();
            EvaluateWifi();
        }) { CheckOnClick = false };

        _setHomeItem = new ToolStripMenuItem("Aktuelles WLAN als Zuhause merken", null, (_, _) =>
        {
            if (_lastWifi.State == WifiState.Connected && _lastWifi.Ssid is { } s)
            {
                _settings.HomeSsid = s;
                _settings.Save();
                _auto.Reset();
                EvaluateWifi();
            }
        });

        _setOfficeItem = new ToolStripMenuItem("Aktuelles WLAN als Buero merken", null, (_, _) =>
        {
            if (_lastWifi.State == WifiState.Connected && _lastWifi.Ssid is { } s)
            {
                _settings.OfficeSsid = s;
                _settings.Save();
                _auto.Reset();
                EvaluateWifi();
            }
        });

        var menu = new ContextMenuStrip();
        menu.Items.AddRange(new ToolStripItem[]
        {
            _ssidHeader,
            new ToolStripSeparator(),
            _toggleItem,
            new ToolStripSeparator(),
            _autoStartItem,
            _stopAwayItem,
            _setHomeItem,
            _setOfficeItem,
            new ToolStripSeparator(),
            new ToolStripMenuItem("Beenden", null, (_, _) => ExitApp())
        });
        menu.Opening += (_, _) => RefreshMenu();

        _notify.ContextMenuStrip = menu;
        _notify.Visible = true;
        _notify.Click += (_, e) =>
        {
            if (e is MouseEventArgs m && m.Button == MouseButtons.Left)
                _tracker.Toggle(); // Push 3 ersetzt das durch das Popup-Panel
        };

        _tracker.Changed += UpdateUi;

        _uiTimer.Interval = 1000;
        _uiTimer.Tick += (_, _) => UpdateUi();
        _uiTimer.Start();

        _wifiTimer.Interval = 5000;
        _wifiTimer.Tick += (_, _) => EvaluateWifi();
        _wifiTimer.Start();

        EvaluateWifi();      // initiale Bewertung
        UpdateUi(force: true);
    }

    private void EvaluateWifi()
    {
        _lastWifi = WifiMonitor.Read();
        _auto.Evaluate(_lastWifi);
    }

    private void RefreshMenu()
    {
        _ssidHeader.Text = _lastWifi.State switch
        {
            WifiState.Connected => $"WLAN: {_lastWifi.Ssid}",
            WifiState.Disconnected => "WLAN: nicht verbunden",
            _ => "WLAN: nicht ermittelbar (Standort-Freigabe?)"
        };

        _autoStartItem.Checked = _settings.AutoStartEnabled;
        _stopAwayItem.Checked = _settings.StopWhenAway;

        var connected = _lastWifi.State == WifiState.Connected;
        _setHomeItem.Enabled = connected;
        _setOfficeItem.Enabled = connected;
        _setHomeItem.Text = _settings.HomeSsid is { } h
            ? $"Aktuelles WLAN als Zuhause merken  (jetzt: {h})"
            : "Aktuelles WLAN als Zuhause merken";
        _setOfficeItem.Text = _settings.OfficeSsid is { } o
            ? $"Aktuelles WLAN als Buero merken  (jetzt: {o})"
            : "Aktuelles WLAN als Buero merken";
    }

    private void UpdateUi() => UpdateUi(false);

    private void UpdateUi(bool force)
    {
        var today = _tracker.TodayTotal();
        var running = _tracker.IsRunning;

        var total = (int)today.TotalSeconds;
        var key = $"{total / 3600}:{(total % 3600) / 60:00}|{running}";
        if (force || key != _lastRendered)
        {
            _lastRendered = key;
            var newIcon = TrayIconRenderer.Render(today, running);
            _notify.Icon = newIcon;
            TrayIconRenderer.Dispose(_currentIcon);
            _currentIcon = newIcon;
        }

        _toggleItem.Text = running ? "Stoppen" : "Starten";

        var status = running && _tracker.RunningSince is { } since
            ? $"laeuft seit {since:HH:mm}"
            : "gestoppt";
        _notify.Text = Trunc($"{Format.Hhmm(today)} heute\n{status}", 63);
    }

    private static string Trunc(string s, int max) => s.Length <= max ? s : s[..max];

    private void ExitApp()
    {
        _uiTimer.Stop();
        _wifiTimer.Stop();
        _notify.Visible = false;
        TrayIconRenderer.Dispose(_currentIcon);
        _notify.Dispose();
        ExitThread();
    }
}
