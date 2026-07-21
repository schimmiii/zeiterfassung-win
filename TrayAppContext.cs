using System.Diagnostics;
using Microsoft.Win32;
using System.Windows.Forms;

namespace Zeiterfassung;

/// <summary>
/// Traegt die App ohne Hauptfenster.
/// Push 1/2: Tray-Icon + WLAN-Auto-Start. Push 3: Popup-Panel (Listen-Navigation).
/// Push 4: Kontextmenue auf Beenden reduziert (Schalter jetzt in den Einstellungen),
/// Sleep/Lock-Pause via SystemEvents.
/// </summary>
public sealed class TrayAppContext : ApplicationContext
{
    private readonly TimeTracker _tracker = new();
    private readonly Settings _settings = Settings.Load();
    private readonly AutoStartController _auto;
    private readonly NotifyIcon _notify = new();
    private readonly System.Windows.Forms.Timer _uiTimer = new();
    private readonly System.Windows.Forms.Timer _wifiTimer = new();
    private readonly PanelForm _panel;

    private readonly ToolStripMenuItem _ssidHeader;
    private readonly ToolStripMenuItem _toggleItem;

    private Icon? _currentIcon;
    private string _lastRendered = "";
    private WifiReading _lastWifi = new(WifiState.Unavailable, null);

    public TrayAppContext()
    {
        _auto = new AutoStartController(_tracker, _settings);

        // Werktags-Regel zentral am Tracker: alle Auto-Pfade (WLAN/Sleep/Lock) laufen
        // durch AutoStart() und werden hierdurch am Wochenende gesperrt.
        _tracker.AutoStartAllowed = () =>
            !(_settings.AutoStartWorkdaysOnly && IsWeekend(DateTime.Now));

        _panel = new PanelForm(
            _tracker, _settings,
            wifi: () => _lastWifi,
            rememberHome: () => RememberCurrent(isHome: true),
            rememberOffice: () => RememberCurrent(isHome: false),
            openLocationSettings: OpenLocationSettings,
            onAutoConfig: () => { _auto.Reset(); EvaluateWifi(); },
            quit: ExitApp);
        // Handle frueh erzeugen (ohne Anzeigen), damit SystemEvents-Callbacks
        // sicher auf den UI-Thread marshallen koennen.
        _ = _panel.Handle;

        // --- Kontextmenue: nur noch Schnellzugriff Start/Stop + Beenden ---
        _ssidHeader = new ToolStripMenuItem("WLAN: —") { Enabled = false };
        _toggleItem = new ToolStripMenuItem("Starten", null, (_, _) => _tracker.Toggle());

        var menu = new ContextMenuStrip();
        menu.Items.AddRange(new ToolStripItem[]
        {
            _ssidHeader,
            new ToolStripSeparator(),
            _toggleItem,
            new ToolStripSeparator(),
            new ToolStripMenuItem("Beenden", null, (_, _) => ExitApp())
        });
        menu.Opening += (_, _) => RefreshMenu();

        _notify.ContextMenuStrip = menu;
        _notify.Visible = true;
        _notify.Click += (_, e) =>
        {
            if (e is MouseEventArgs m && m.Button == MouseButtons.Left)
                _panel.Toggle();
        };

        _tracker.Changed += () => { UpdateUi(); _panel.RefreshContent(); };

        _uiTimer.Interval = 1000;
        _uiTimer.Tick += (_, _) => UpdateUi();
        _uiTimer.Start();

        _wifiTimer.Interval = 5000;
        _wifiTimer.Tick += (_, _) => EvaluateWifi();
        _wifiTimer.Start();

        // Sleep/Lock → Pause; Wake/Unlock → Resume.
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;

        EvaluateWifi();
        UpdateUi(force: true);
    }

    private void RememberCurrent(bool isHome)
    {
        if (_lastWifi is { State: WifiState.Connected, Ssid: { } s })
        {
            if (isHome) _settings.HomeSsid = s; else _settings.OfficeSsid = s;
            _settings.Save();
            _auto.Reset();
            EvaluateWifi();
        }
    }

    private static void OpenLocationSettings()
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:privacy-location") { UseShellExecute = true }); }
        catch { /* nicht fatal */ }
    }

    // MARK: Sleep/Lock (Callbacks kommen ggf. vom System-Thread → auf UI marshallen)

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend) Ui(_tracker.PauseForSystem);
        else if (e.Mode == PowerModes.Resume) Ui(_tracker.ResumeFromSystem);
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionLock) Ui(_tracker.PauseForSystem);
        else if (e.Reason == SessionSwitchReason.SessionUnlock) Ui(_tracker.ResumeFromSystem);
    }

    private void Ui(Action a)
    {
        if (_panel.IsHandleCreated) _panel.BeginInvoke(a);
        else a();
    }

    private void EvaluateWifi()
    {
        _lastWifi = WifiMonitor.Read();
        _auto.Evaluate(_lastWifi);
        _panel.RefreshContent();
    }

    private void RefreshMenu()
    {
        _ssidHeader.Text = _lastWifi.State switch
        {
            WifiState.Connected => $"WLAN: {_lastWifi.Ssid}",
            WifiState.Disconnected => "WLAN: nicht verbunden",
            _ => "WLAN: nicht ermittelbar (Standort-Freigabe?)"
        };
        _toggleItem.Text = _tracker.IsRunning ? "Stoppen" : "Starten";
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

        var status = running && _tracker.RunningSince is { } since
            ? $"laeuft seit {since:HH:mm}"
            : "gestoppt";
        _notify.Text = Trunc($"{Format.Hhmm(today)} heute\n{status}", 63);
    }

    private static string Trunc(string s, int max) => s.Length <= max ? s : s[..max];

    private static bool IsWeekend(DateTime d) =>
        d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    private void ExitApp()
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        _uiTimer.Stop();
        _wifiTimer.Stop();
        _notify.Visible = false;
        TrayIconRenderer.Dispose(_currentIcon);
        _panel.Dispose();
        _notify.Dispose();
        ExitThread();
    }
}
