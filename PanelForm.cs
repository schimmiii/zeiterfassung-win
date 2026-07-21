using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

namespace Zeiterfassung;

/// <summary>
/// Zentrale Icon-Glyphen (Segoe Fluent Icons / Segoe MDL2 Assets).
/// HINWEIS: Codepoints auf Linux nicht renderbar — falls ein Glyph auf echtem
/// Windows falsch aussieht, hier justieren. Fallback-Font ist MDL2 (Win10).
/// </summary>
internal static class Glyphs
{
    public const string Timer = "\uE916";
    public const string Play = "\uE768";
    public const string Pause = "\uE769";
    public const string Home = "\uE80F";
    public const string Office = "\uE825"; // Bank/Gebaeude
    public const string Plane = "\uE709";
    public const string Chart = "\uE9D2";
    public const string Calendar = "\uE787";
    public const string Upload = "\uE898";
    public const string Edit = "\uE70F";
    public const string Settings = "\uE713";
    public const string Power = "\uE7E8";
    public const string Delete = "\uE74D";
    public const string Wifi = "\uE701";
    public const string Clock = "\uE917";
    public const string Location = "\uE81D";
    public const string ChevronRight = "\uE76C";
    public const string ChevronLeft = "\uE76B";
    public const string Away = "\uE706";        // Sonne \u2014 Abwesenheit/Urlaub
}

internal static class Theme
{
    public static readonly Color Bg = Color.FromArgb(250, 250, 252);
    public static readonly Color Ink = Color.FromArgb(28, 30, 34);
    public static readonly Color Sub = Color.FromArgb(120, 124, 132);
    public static readonly Color Tert = Color.FromArgb(176, 180, 188);
    public static readonly Color Accent = Color.FromArgb(47, 128, 255);
    public static readonly Color Red = Color.FromArgb(210, 64, 64);
    public static readonly Color Green = Color.FromArgb(40, 150, 84);
    public static readonly Color Line = Color.FromArgb(228, 230, 234);
    public static readonly Color Hover = Color.FromArgb(242, 243, 245);
    public static readonly Color TrackOff = Color.FromArgb(206, 210, 216);
}

/// <summary>
/// Borderloses Tray-Popup mit Listen-Navigation (wie die macOS-App).
/// Push 3a: Home (STATUS + Saldo), Woche, Nachtragen. Uebersicht/Export/
/// Einstellungen sind Platzhalter (Push 3b/4).
/// </summary>
public sealed class PanelForm : Form
{
    public enum Screen { Home, Woche, Uebersicht, Export, Nachtragen, Abwesenheit, Einstellungen }

    private readonly TimeTracker _tracker;
    private readonly Settings _settings;
    private readonly Func<WifiReading> _wifi;
    private readonly Action _rememberHome;
    private readonly Action _rememberOffice;
    private readonly Action _openLocationSettings;
    private readonly Action _onAutoConfig;
    private readonly Action _quit;

    private readonly FlowLayoutPanel _content;
    private readonly System.Windows.Forms.Timer _live = new() { Interval = 1000 };
    private readonly Font _iconFont;
    private readonly Font _iconSmall;

    private Screen _screen = Screen.Home;

    // Übersicht-Zustand (ueberlebt Rebuild)
    private bool _ovMonatScope = true;
    private DateTime _ovMonth = new(DateTime.Now.Year, DateTime.Now.Month, 1);
    private int _ovYear = DateTime.Now.Year;

    // Export-Zustand
    private int _exScopeIdx = 0;   // 0 Monat, 1 Jahr, 2 Alles
    private bool _exFormatCsv = true;
    private bool _exDetailed = false;
    private DateTime _exMonth = new(DateTime.Now.Year, DateTime.Now.Month, 1);
    private int _exYear = DateTime.Now.Year;

    // Unterdrueckt das Schliessen-bei-Fokusverlust waehrend eines modalen Dialogs.
    private bool _suppressHide;

    // Live-Referenzen (pro Screen gesetzt), damit der Sekundentakt ohne Rebuild aktualisiert.
    private Label? _liveToday, _liveSince, _liveSaldo, _liveWeekTotal;

    private const int ContentW = 274;

    public PanelForm(TimeTracker tracker, Settings settings, Func<WifiReading> wifi,
                     Action rememberHome, Action rememberOffice,
                     Action openLocationSettings, Action onAutoConfig, Action quit)
    {
        _tracker = tracker;
        _settings = settings;
        _wifi = wifi;
        _rememberHome = rememberHome;
        _rememberOffice = rememberOffice;
        _openLocationSettings = openLocationSettings;
        _onAutoConfig = onAutoConfig;
        _quit = quit;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        // None: WinForms skaliert NICHT automatisch. Wir bauen logisch @96 und skalieren
        // den Baum selbst (ScaleTree) -> kein Doppel-Skalieren gegen die Font-DPI.
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Theme.Bg;
        Font = new Font("Segoe UI", 9f);
        ClientSize = new Size(300, 520);
        DoubleBuffered = true;

        // 1px-Rahmen fuers Popup.
        Padding = new Padding(1);
        BackColor = Theme.Line; // Rahmenfarbe; inneres Panel ueberdeckt mit Bg

        _iconFont = MakeIconFont(14f);
        _iconSmall = MakeIconFont(11f);

        _content = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = Theme.Bg,
            Padding = new Padding(0, 6, 0, 8)
        };
        Controls.Add(_content);

        _live.Tick += (_, _) => Tick();
    }

    private static Font MakeIconFont(float size)
    {
        // Fluent (Win11) mit Fallback MDL2 (Win10). Existiert keins, nimmt WinForms den Default.
        try { return new Font("Segoe Fluent Icons", size); }
        catch { return new Font("Segoe MDL2 Assets", size); }
    }

    // MARK: DPI-Skalierung (Weg A)
    // Layout wird logisch @96 gebaut und pro Rebuild einmal auf die reale DPI skaliert.
    // Fonts skalieren GDI-seitig ueber ihre Punktgroesse selbst -> hier NUR Geometrie.
    private float _scale = 1f;
    private int Sc(int px) => (int)Math.Round(px * _scale);
    private static int Rnd(int v, float f) => (int)Math.Round(v * f);

    /// <summary>Skaliert Size/Margin/Padding aller Kinder rekursiv; bei absoluter
    /// Anordnung auch die Position. In FlowLayoutPanels steuert der Flow die Position,
    /// dort nur Size/Margin. Fonts bleiben unangetastet.</summary>
    private void ScaleTree(Control parent, float f)
    {
        bool flow = parent is FlowLayoutPanel;
        foreach (Control c in parent.Controls)
        {
            var m = c.Margin;
            c.Margin = new Padding(Rnd(m.Left, f), Rnd(m.Top, f), Rnd(m.Right, f), Rnd(m.Bottom, f));
            var p = c.Padding;
            c.Padding = new Padding(Rnd(p.Left, f), Rnd(p.Top, f), Rnd(p.Right, f), Rnd(p.Bottom, f));
            if (flow)
                c.Size = new Size(Rnd(c.Width, f), Rnd(c.Height, f));
            else
                c.Bounds = new Rectangle(Rnd(c.Left, f), Rnd(c.Top, f), Rnd(c.Width, f), Rnd(c.Height, f));
            if (c.HasChildren) ScaleTree(c, f);
        }
    }

    // MARK: Anzeigen / Positionieren / Schliessen

    public void Toggle()
    {
        if (Visible) Hide();
        else ShowNearTray();
    }

    private void ShowNearTray()
    {
        _ = Handle;                                    // Handle erzwingen -> DeviceDpi gueltig
        _scale = DeviceDpi / 96f;                      // 1.0 @100%, 1.25 @125%, 1.5 @150%
        ClientSize = new Size(Sc(300), Sc(520));       // Fenster fuer skalierte Inhalte
        _content.Padding = new Padding(0, Sc(6), 0, Sc(8));   // aus Konstanten -> nicht kumulativ
        _screen = Screen.Home;
        Rebuild();
        var wa = Screen_FromCursor().WorkingArea;
        Location = new Point(wa.Right - Width - Sc(8), wa.Bottom - Height - Sc(8));
        Show();
        Activate();
        _live.Start();
    }

    private static System.Windows.Forms.Screen Screen_FromCursor() =>
        System.Windows.Forms.Screen.FromPoint(Cursor.Position);

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (_suppressHide) return;
        Hide();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (!Visible) _live.Stop();
    }

    /// <summary>Von aussen aufrufbar (Tracker geaendert) — nur wenn sichtbar.</summary>
    public void RefreshContent()
    {
        if (Visible) Rebuild();
    }

    private void Navigate(Screen s) { _screen = s; Rebuild(); }

    private void Tick()
    {
        var now = DateTime.Now;
        _liveToday?.SetTextIfChanged($"{Format.Hhmm(_tracker.TodayTotal())} \u00b7 heute");
        if (_liveSince is not null)
            _liveSince.Text = _tracker.RunningSince is { } s ? $"seit {s:HH:mm}" : "";
        if (_liveSaldo is not null)
        {
            var saldo = _tracker.SaldoKumuliert(_settings.Wochenstunden, _settings.AnfangssaldoH,
                _settings.KontostartOr(_tracker.EarliestStart));
            _liveSaldo.Text = Format.Signed(saldo);
            _liveSaldo.ForeColor = saldo.Ticks < 0 ? Theme.Red : Theme.Green;
        }
        _liveWeekTotal?.SetTextIfChanged(Format.Hhmm(_tracker.WeekTotal()));
    }

    // MARK: Rebuild-Dispatch

    private void Rebuild()
    {
        _liveToday = _liveSince = _liveSaldo = _liveWeekTotal = null;
        _content.SuspendLayout();
        _content.Controls.Clear();
        switch (_screen)
        {
            case Screen.Home: BuildHome(); break;
            case Screen.Woche: BuildWoche(); break;
            case Screen.Nachtragen: BuildNachtragen(); break;
            case Screen.Abwesenheit: BuildAbwesenheit(); break;
            case Screen.Uebersicht: BuildUebersicht(); break;
            case Screen.Export: BuildExport(); break;
            case Screen.Einstellungen: BuildEinstellungen(); break;
        }
        if (_scale != 1f) ScaleTree(_content, _scale);   // frisch gebaute Controls einmal skalieren
        _content.ResumeLayout();
    }

    // MARK: Home

    private void BuildHome()
    {
        var reading = _wifi();
        var loc = LocationClassifier.Classify(reading, _settings);
        var running = _tracker.IsRunning;

        // Header
        var header = NewRow(46);
        var hIcon = IconLabel(Glyphs.Timer, Theme.Sub, _iconFont); hIcon.SetBounds(12, 8, 22, 24);
        _liveToday = Text14(($"{Format.Hhmm(_tracker.TodayTotal())} \u00b7 heute"), Theme.Ink, bold: true);
        _liveToday.SetBounds(40, 7, ContentW - 48, 18);
        var status = Text11(StatusLine(loc, reading), Theme.Sub);
        status.SetBounds(40, 25, ContentW - 48, 15);
        header.Controls.AddRange(new Control[] { hIcon, _liveToday, status });
        _content.Controls.Add(header);

        _content.Controls.Add(Divider());

        // Start/Stop
        _liveSince = Text12(running && _tracker.RunningSince is { } rs ? $"seit {rs:HH:mm}" : "", Theme.Sub);
        _content.Controls.Add(ActionRow(running ? Glyphs.Pause : Glyphs.Play,
            running ? "Stoppen" : "Starten", trailing: _liveSince, bold: true,
            onClick: () => { _tracker.Toggle(); Rebuild(); }));

        _content.Controls.Add(Divider());
        _content.Controls.Add(SectionLabel("STATUS"));

        if (loc == WorkLocation.KeineBerechtigung)
            _content.Controls.Add(ActionRow(Glyphs.Location, "Standort-Freigabe öffnen",
                trailing: null, onClick: _openLocationSettings));

        _content.Controls.Add(StatusLocationRow(Glyphs.Home, "Zuhause", WorkLocation.Zuhause, loc, reading, _rememberHome));
        _content.Controls.Add(StatusLocationRow(Glyphs.Office, "Büro", WorkLocation.Buero, loc, reading, _rememberOffice));
        _content.Controls.Add(StatusLocationRow(Glyphs.Plane, "Auswärts", WorkLocation.Auswaerts, loc, reading, null));

        _content.Controls.Add(Divider());

        // Woche (nav) mit Wert
        _liveWeekTotal = Text12(Format.Hhmm(_tracker.WeekTotal()), Theme.Sub);
        _content.Controls.Add(ActionRow(Glyphs.Chart, "Woche", trailing: _liveWeekTotal, chevron: true,
            onClick: () => Navigate(Screen.Woche)));

        // Saldo (Wert, nicht klickbar)
        var saldo = _tracker.SaldoKumuliert(_settings.Wochenstunden, _settings.AnfangssaldoH,
            _settings.KontostartOr(_tracker.EarliestStart));
        _liveSaldo = Text12(Format.Signed(saldo), saldo.Ticks < 0 ? Theme.Red : Theme.Green, bold: true);
        _content.Controls.Add(StatusRow(SaldoGlyphLabel(), "Saldo", _liveSaldo));

        _content.Controls.Add(ActionRow(Glyphs.Calendar, "Übersicht", chevron: true, onClick: () => Navigate(Screen.Uebersicht)));
        _content.Controls.Add(ActionRow(Glyphs.Upload, "Exportieren…", chevron: true, onClick: () => Navigate(Screen.Export)));
        _content.Controls.Add(ActionRow(Glyphs.Edit, "Nachtragen…", chevron: true, onClick: () => Navigate(Screen.Nachtragen)));
        _content.Controls.Add(ActionRow(Glyphs.Away, "Abwesenheit…", chevron: true, onClick: () => Navigate(Screen.Abwesenheit)));
        _content.Controls.Add(ActionRow(Glyphs.Settings, "Einstellungen", chevron: true, onClick: () => Navigate(Screen.Einstellungen)));

        _content.Controls.Add(Divider());
        _content.Controls.Add(ActionRow(Glyphs.Power, "Beenden", onClick: _quit));
    }

    private string StatusLine(WorkLocation loc, WifiReading r) => loc switch
    {
        WorkLocation.Zuhause => $"Zuhause \u00b7 {r.Ssid}",
        WorkLocation.Buero => $"Büro \u00b7 {r.Ssid}",
        WorkLocation.Auswaerts => $"Auswärts \u00b7 {r.Ssid}",
        WorkLocation.KeinWLAN => "kein WLAN verbunden",
        _ => "Standort-Freigabe nötig"
    };

    /// <summary>„aktiv"-Badge wenn dieser Ort erkannt ist, sonst „merken"-Button (falls SSID lesbar).</summary>
    private Control StatusLocationRow(string glyph, string title, WorkLocation kind,
        WorkLocation current, WifiReading reading, Action? remember)
    {
        Control trailing;
        if (current == kind)
        {
            var badge = new Panel { Size = new Size(52, 20), BackColor = Color.Transparent };
            var dot = new Panel { Size = new Size(7, 7), Location = new Point(0, 7), BackColor = Theme.Green };
            MakeRound(dot);
            var lbl = Text11("aktiv", Theme.Sub); lbl.SetBounds(12, 3, 40, 15);
            badge.Controls.Add(dot); badge.Controls.Add(lbl);
            trailing = badge;
        }
        else if (remember is not null && reading.State == WifiState.Connected)
        {
            var b = new Button
            {
                Text = "merken", AutoSize = false, Size = new Size(56, 20),
                FlatStyle = FlatStyle.Flat, ForeColor = Theme.Accent, BackColor = Theme.Bg,
                Font = new Font("Segoe UI", 8.5f)
            };
            b.FlatAppearance.BorderColor = Theme.Accent;
            b.Click += (_, _) => { remember(); Rebuild(); };
            trailing = b;
        }
        else
        {
            trailing = new Panel { Size = new Size(1, 1), BackColor = Color.Transparent };
        }
        return StatusRow(IconLabel(glyph, Theme.Sub, _iconFont), title, trailing);
    }

    private Label SaldoGlyphLabel()
    {
        // „±" aus der normalen Schrift (kein Icon-Font noetig).
        var l = new Label { Text = "\u00b1", AutoSize = false, Size = new Size(22, 22), ForeColor = Theme.Sub,
            Font = new Font("Segoe UI", 12f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter };
        return l;
    }

    // MARK: Woche

    private void BuildWoche()
    {
        _content.Controls.Add(SubHeader("Woche"));
        _content.Controls.Add(Divider());

        var days = _tracker.WeekBreakdown();
        var max = days.Count == 0 ? TimeSpan.FromHours(1)
            : TimeSpan.FromTicks(Math.Max(days.Max(d => d.Total.Ticks), TimeSpan.FromHours(1).Ticks));

        foreach (var d in days)
        {
            var row = NewRow(24);
            var dn = Text12(d.Date.ToString("ddd", System.Globalization.CultureInfo.CurrentCulture), Theme.Sub, bold: true);
            dn.SetBounds(14, 4, 36, 16);
            if (d.Date.Date == DateTime.Today) dn.ForeColor = Theme.Ink;

            var track = new Panel { BackColor = Color.Transparent };
            track.SetBounds(52, 8, ContentW - 52 - 66, 8);
            var frac = max.Ticks == 0 ? 0 : (double)d.Total.Ticks / max.Ticks;
            var fillW = d.Total > TimeSpan.Zero ? Math.Max((int)(track.Width * frac), 6) : 0;
            var fill = new Panel { BackColor = d.Total > TimeSpan.Zero ? Theme.Accent : Theme.Line,
                Size = new Size(Math.Max(fillW, 2), 6), Location = new Point(0, 1) };
            track.Controls.Add(fill);

            var absence = _tracker.AbsenceTypeFor(d.Date);
            var val = Text11(absence is { } at ? at.Emoji()
                                               : (d.Total > TimeSpan.Zero ? Format.Hhmm(d.Total) : "–"), Theme.Sub);
            val.SetBounds(ContentW - 62, 5, 58, 15); val.TextAlign = ContentAlignment.MiddleRight;

            row.Controls.AddRange(new Control[] { dn, track, val });
            _content.Controls.Add(row);
        }

        _content.Controls.Add(Divider());
        var total = NewRow(26);
        var lbl = Text12("Woche gesamt", Theme.Ink, bold: true); lbl.SetBounds(14, 5, 120, 16);
        var tv = Text12(Format.Hhmm(_tracker.WeekTotal()), Theme.Ink, bold: true);
        tv.SetBounds(ContentW - 90, 5, 76, 16); tv.TextAlign = ContentAlignment.MiddleRight;
        total.Controls.Add(lbl); total.Controls.Add(tv);
        _content.Controls.Add(total);
    }

    // MARK: Nachtragen

    private DateTimePicker? _dpDay, _dpVon, _dpBis;
    private Button? _addBtn;
    private DateTime _ntDay = DateTime.Today;   // gewaehltes Nachtragen-Datum (ueberlebt Rebuild)

    private void BuildNachtragen()
    {
        _content.Controls.Add(SubHeader("Nachtragen"));
        _content.Controls.Add(Divider());

        _dpDay = FieldRow("Datum", out var r1); _dpDay.Format = DateTimePickerFormat.Short;
        _dpDay.Value = _ntDay;   // Auswahl wiederherstellen — VOR dem Handler, sonst Rebuild-Schleife
        _content.Controls.Add(r1);
        _dpVon = FieldRow("Von", out var r2); TimePicker(_dpVon, 9, 0);
        _content.Controls.Add(r2);
        _dpBis = FieldRow("Bis", out var r3); TimePicker(_dpBis, 17, 0);
        _content.Controls.Add(r3);

        _addBtn = new Button
        {
            AutoSize = false, Size = new Size(ContentW - 28, 32), FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Accent, ForeColor = Color.White, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
        };
        _addBtn.FlatAppearance.BorderSize = 0;
        _addBtn.Margin = new Padding(14, 6, 0, 6);
        _addBtn.Click += (_, _) => AddManual();
        _dpVon.ValueChanged += (_, _) => UpdateAddButton();
        _dpBis.ValueChanged += (_, _) => UpdateAddButton();
        _dpDay.ValueChanged += (_, _) => { _ntDay = _dpDay.Value.Date; Rebuild(); };   // Datumwechsel -> Liste neu
        UpdateAddButton();
        _content.Controls.Add(_addBtn);

        _content.Controls.Add(Divider());
        _content.Controls.Add(SectionLabel(NtDayLabel()));

        var daySegs = _tracker.DaySegmentsFor(_ntDay);
        if (daySegs.Count == 0)
        {
            var none = Text12("keine Segmente an diesem Tag", Theme.Tert);
            var row = NewRow(22); none.SetBounds(14, 3, 220, 16); row.Controls.Add(none);
            _content.Controls.Add(row);
        }
        else
        {
            foreach (var seg in daySegs)
            {
                var row = NewRow(24);
                var range = Text12($"{seg.Start:HH:mm} – {(seg.End is { } e ? e.ToString("HH:mm") : "…")}", Theme.Ink);
                range.SetBounds(14, 4, 120, 16);
                var dur = Text11(Format.Hhmm((seg.End ?? DateTime.Now) - seg.Start), Theme.Sub);
                dur.SetBounds(ContentW - 130, 5, 74, 15); dur.TextAlign = ContentAlignment.MiddleRight;
                row.Controls.Add(range); row.Controls.Add(dur);

                if (seg.End is null)
                {
                    var run = Text11("läuft", Theme.Green); run.SetBounds(ContentW - 44, 5, 40, 15);
                    row.Controls.Add(run);
                }
                else
                {
                    var del = IconLabel(Glyphs.Delete, Theme.Sub, _iconSmall);
                    del.SetBounds(ContentW - 32, 4, 18, 16); del.Cursor = Cursors.Hand;
                    var id = seg.Id;
                    del.Click += (_, _) => { _tracker.Delete(id); Rebuild(); };
                    row.Controls.Add(del);
                }
                _content.Controls.Add(row);
            }
        }
    }

    private void TimePicker(DateTimePicker dp, int h, int m)
    {
        dp.Format = DateTimePickerFormat.Time;
        dp.ShowUpDown = true;
        dp.Value = DateTime.Today.AddHours(h).AddMinutes(m);
    }

    private (DateTime start, DateTime end) NachtragenRange()
    {
        var day = _dpDay!.Value.Date;
        var von = day.AddHours(_dpVon!.Value.Hour).AddMinutes(_dpVon.Value.Minute);
        var bis = day.AddHours(_dpBis!.Value.Hour).AddMinutes(_dpBis.Value.Minute);
        return (von, bis);
    }

    private void UpdateAddButton()
    {
        if (_addBtn is null) return;
        var (s, e) = NachtragenRange();
        var valid = e > s;
        _addBtn.Enabled = valid;
        _addBtn.Text = valid ? $"Hinzufügen ({Format.Hhmm(e - s)})" : "Bis muss nach Von liegen";
        _addBtn.BackColor = valid ? Theme.Accent : Theme.TrackOff;
    }

    private void AddManual()
    {
        var (s, e) = NachtragenRange();
        _tracker.AddManual(s, e);
        Rebuild();
    }

    /// <summary>Ueberschrift der Segmentliste: „HEUTE" am heutigen Tag, sonst Wochentag + Datum.</summary>
    private string NtDayLabel()
    {
        if (_ntDay.Date == DateTime.Today) return "HEUTE";
        return _ntDay.ToString("ddd, dd.MM.yyyy", CultureInfo.CurrentCulture).ToUpperInvariant();
    }

    private DateTimePicker FieldRow(string label, out Panel row)
    {
        row = NewRow(30);
        var lbl = Text12(label, Theme.Ink); lbl.SetBounds(14, 7, 60, 16);
        var dp = new DateTimePicker { Width = 120, Font = new Font("Segoe UI", 9f) };
        dp.Location = new Point(ContentW - 120 - 14, 4);
        row.Controls.Add(lbl); row.Controls.Add(dp);
        return dp;
    }

    // MARK: Abwesenheit (Urlaub / Krank / Feiertag)

    private DateTimePicker? _dpAbsVon, _dpAbsBis;
    private Button? _absAddBtn;
    private AbsenceType _absType = AbsenceType.Urlaub;
    private DateTime _absVon = DateTime.Today;   // ueberleben Rebuild (Typ-Umschalter/Datumwechsel)
    private DateTime _absBis = DateTime.Today;

    private void BuildAbwesenheit()
    {
        _content.Controls.Add(SubHeader("Abwesenheit"));
        _content.Controls.Add(Divider());

        _content.Controls.Add(Segmented(new[] { "Urlaub", "Krank", "Feiertag" }, (int)_absType,
            i => { _absType = (AbsenceType)i; Rebuild(); }));

        _dpAbsVon = FieldRow("Von", out var rv); _dpAbsVon.Format = DateTimePickerFormat.Short;
        _dpAbsVon.Value = _absVon;   // VOR dem Handler setzen, sonst Rebuild-Schleife
        _content.Controls.Add(rv);
        _dpAbsBis = FieldRow("Bis", out var rb); _dpAbsBis.Format = DateTimePickerFormat.Short;
        _dpAbsBis.Value = _absBis;
        _content.Controls.Add(rb);

        _dpAbsVon.ValueChanged += (_, _) =>
        {
            _absVon = _dpAbsVon.Value.Date;
            if (_absBis < _absVon) { _absBis = _absVon; _dpAbsBis!.Value = _absBis; }
            UpdateAbsButton();
        };
        _dpAbsBis.ValueChanged += (_, _) => { _absBis = _dpAbsBis.Value.Date; UpdateAbsButton(); };

        _absAddBtn = new Button
        {
            AutoSize = false, Size = new Size(ContentW - 28, 32), FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Accent, ForeColor = Color.White, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
        };
        _absAddBtn.FlatAppearance.BorderSize = 0;
        _absAddBtn.Margin = new Padding(14, 6, 0, 6);
        _absAddBtn.Click += (_, _) => { _tracker.SetAbsence(_absVon, _absBis, _absType); Rebuild(); };
        UpdateAbsButton();
        _content.Controls.Add(_absAddBtn);

        _content.Controls.Add(Divider());

        // Kopfzeile: Jahr + Zaehler je Typ (nur Typen mit >0)
        int year = DateTime.Today.Year;
        var counts = _tracker.AbsenceCounts(year);
        var headRow = NewRow(20);
        var yl = Text11(year.ToString(), Theme.Tert); yl.SetBounds(14, 2, 60, 16);
        var parts = new List<string>();
        foreach (AbsenceType t in Enum.GetValues<AbsenceType>())
            if (counts.TryGetValue(t, out var c) && c > 0) parts.Add($"{t.Emoji()}{c}");
        var cl = Text11(string.Join("   ", parts), Theme.Sub);
        cl.SetBounds(ContentW - 174, 2, 160, 16); cl.TextAlign = ContentAlignment.MiddleRight;
        headRow.Controls.Add(yl); headRow.Controls.Add(cl);
        _content.Controls.Add(headRow);

        var runs = _tracker.AbsenceRuns();
        if (runs.Count == 0)
        {
            var none = Text12("keine Abwesenheiten erfasst", Theme.Tert);
            var row = NewRow(22); none.SetBounds(14, 3, 220, 16); row.Controls.Add(none);
            _content.Controls.Add(row);
        }
        else
        {
            foreach (var run in runs)
            {
                var row = NewRow(24);
                var lbl = Text12($"{run.Type.Emoji()}  {RunLabel(run)}", Theme.Ink);
                lbl.SetBounds(14, 4, 170, 16);
                var days = Text11($"{run.Workdays} T", Theme.Sub);
                days.SetBounds(ContentW - 74, 5, 40, 15); days.TextAlign = ContentAlignment.MiddleRight;
                var del = IconLabel(Glyphs.Delete, Theme.Sub, _iconSmall);
                del.SetBounds(ContentW - 32, 4, 18, 16); del.Cursor = Cursors.Hand;
                DateTime s = run.Start, e = run.End;
                del.Click += (_, _) => { _tracker.ClearAbsence(s, e); Rebuild(); };
                row.Controls.Add(lbl); row.Controls.Add(days); row.Controls.Add(del);
                _content.Controls.Add(row);
            }
        }
    }

    private void UpdateAbsButton()
    {
        if (_absAddBtn is null) return;
        var n = _tracker.WorkdayCount(_absVon, _absBis);
        var valid = n > 0;
        _absAddBtn.Enabled = valid;
        _absAddBtn.Text = valid ? $"Eintragen ({n} {(n == 1 ? "Werktag" : "Werktage")})" : "Kein Werktag im Zeitraum";
        _absAddBtn.BackColor = valid ? Theme.Accent : Theme.TrackOff;
    }

    /// <summary>„Mo 20.07 – Mo 27.07" bzw. „Do 02.01" bei Einzeltag.</summary>
    private string RunLabel(AbsenceRun run)
    {
        const string f = "ddd dd.MM";
        var ci = CultureInfo.CurrentCulture;
        return run.Start.Date == run.End.Date
            ? run.Start.ToString(f, ci)
            : $"{run.Start.ToString(f, ci)} – {run.End.ToString(f, ci)}";
    }

    // MARK: Übersicht (Monat / Jahr)

    private void BuildUebersicht()
    {
        _content.Controls.Add(SubHeader("Übersicht"));
        _content.Controls.Add(Divider());
        _content.Controls.Add(Segmented(new[] { "Monat", "Jahr" }, _ovMonatScope ? 0 : 1,
            i => { _ovMonatScope = i == 0; Rebuild(); }));
        _content.Controls.Add(Stepper(OvLabel(), OvCanForward(),
            back: () => { OvStep(-1); Rebuild(); }, fwd: () => { OvStep(1); Rebuild(); }));
        _content.Controls.Add(Divider());

        var rows = _ovMonatScope ? _tracker.MonthBreakdown(_ovMonth) : _tracker.YearBreakdown(_ovYear);
        var max = rows.Count == 0 ? TimeSpan.FromHours(1)
            : TimeSpan.FromTicks(Math.Max(rows.Max(r => r.Total.Ticks), TimeSpan.FromHours(1).Ticks));

        foreach (var r in rows)
        {
            var row = NewRow(24);
            var lab = Text12(r.Label, Theme.Sub, bold: true); lab.SetBounds(14, 4, 46, 16);
            var track = new Panel { BackColor = Color.Transparent };
            track.SetBounds(62, 8, ContentW - 62 - 66, 8);
            var frac = max.Ticks == 0 ? 0 : (double)r.Total.Ticks / max.Ticks;
            var fillW = r.Total > TimeSpan.Zero ? Math.Max((int)(track.Width * frac), 6) : 0;
            var fill = new Panel { BackColor = r.Total > TimeSpan.Zero ? Theme.Accent : Theme.Line,
                Size = new Size(Math.Max(fillW, 2), 6), Location = new Point(0, 1) };
            track.Controls.Add(fill);
            var val = Text11(r.Total > TimeSpan.Zero ? Format.Hhmm(r.Total) : "–", Theme.Sub);
            val.SetBounds(ContentW - 62, 5, 58, 15); val.TextAlign = ContentAlignment.MiddleRight;
            row.Controls.AddRange(new Control[] { lab, track, val });
            _content.Controls.Add(row);
        }

        _content.Controls.Add(Divider());
        var total = NewRow(26);
        var tl = Text12(_ovMonatScope ? "Monat gesamt" : "Jahr gesamt", Theme.Ink, bold: true);
        tl.SetBounds(14, 5, 120, 16);
        var sum = rows.Aggregate(TimeSpan.Zero, (a, r) => a + r.Total);
        var tv = Text12(Format.Hhmm(sum), Theme.Ink, bold: true);
        tv.SetBounds(ContentW - 90, 5, 76, 16); tv.TextAlign = ContentAlignment.MiddleRight;
        total.Controls.Add(tl); total.Controls.Add(tv);
        _content.Controls.Add(total);
    }

    private string OvLabel() => _ovMonatScope
        ? _ovMonth.ToString("MMMM yyyy", CultureInfo.CurrentCulture)
        : _ovYear.ToString();

    private void OvStep(int d) { if (_ovMonatScope) _ovMonth = _ovMonth.AddMonths(d); else _ovYear += d; }

    private bool OvCanForward()
    {
        if (_ovMonatScope)
        {
            var cur = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            return _ovMonth < cur;
        }
        return _ovYear < DateTime.Now.Year;
    }

    // MARK: Exportieren (CSV / XLSX)

    private void BuildExport()
    {
        _content.Controls.Add(SubHeader("Exportieren"));
        _content.Controls.Add(Divider());
        _content.Controls.Add(Segmented(new[] { "Monat", "Jahr", "Alles" }, _exScopeIdx,
            i => { _exScopeIdx = i; Rebuild(); }));

        if (_exScopeIdx == 0) _content.Controls.Add(MonthCombo());
        else if (_exScopeIdx == 1) _content.Controls.Add(YearCombo());
        else
        {
            var r = NewRow(24);
            var l = Text12("Gesamter erfasster Zeitraum", Theme.Sub); l.SetBounds(14, 4, 240, 16);
            r.Controls.Add(l); _content.Controls.Add(r);
        }

        _content.Controls.Add(Divider());
        _content.Controls.Add(Segmented(new[] { "CSV", "XLSX" }, _exFormatCsv ? 0 : 1,
            i => { _exFormatCsv = i == 0; Rebuild(); }));

        var cbRow = NewRow(24);
        var cb = new CheckBox
        {
            Text = "Detailliert (Segmente)", Checked = _exDetailed, AutoSize = false,
            Size = new Size(ContentW - 28, 20), Location = new Point(12, 2),
            Font = new Font("Segoe UI", 9.5f), ForeColor = Theme.Ink, BackColor = Color.Transparent
        };
        cb.CheckedChanged += (_, _) => _exDetailed = cb.Checked;
        cbRow.Controls.Add(cb);
        _content.Controls.Add(cbRow);

        var iv = ExInterval();
        var segs = _tracker.SegmentsIn(iv);
        var totalSpan = segs.Aggregate(TimeSpan.Zero, (a, s) => a + ((s.End ?? DateTime.Now) - s.Start));
        var info = NewRow(24);
        var cnt = Text12($"{segs.Count} Segment{(segs.Count == 1 ? "" : "e")}", Theme.Sub);
        cnt.SetBounds(14, 4, 140, 16);
        var tot = Text12(Format.Hhmm(totalSpan), Theme.Ink, bold: true);
        tot.SetBounds(ContentW - 90, 4, 76, 16); tot.TextAlign = ContentAlignment.MiddleRight;
        info.Controls.Add(cnt); info.Controls.Add(tot);
        _content.Controls.Add(info);

        var btn = new Button
        {
            AutoSize = false, Size = new Size(ContentW - 28, 32), FlatStyle = FlatStyle.Flat,
            BackColor = segs.Count > 0 ? Theme.Accent : Theme.TrackOff, ForeColor = Color.White,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), Margin = new Padding(14, 4, 0, 6),
            Enabled = segs.Count > 0, Text = $"Als {(_exFormatCsv ? "CSV" : "XLSX")} speichern…"
        };
        btn.FlatAppearance.BorderSize = 0;
        btn.Click += (_, _) => ExportSave(iv);
        _content.Controls.Add(btn);
    }

    private DateInterval ExInterval()
    {
        switch (_exScopeIdx)
        {
            case 0:
                var ms = new DateTime(_exMonth.Year, _exMonth.Month, 1);
                return new DateInterval(ms, ms.AddMonths(1));
            case 1:
                return new DateInterval(new DateTime(_exYear, 1, 1), new DateTime(_exYear + 1, 1, 1));
            default:
                var start = _tracker.EarliestStart ?? DateTime.Now;
                return new DateInterval(start, DateTime.MaxValue);
        }
    }

    private Control MonthCombo()
    {
        var row = NewRow(30);
        var months = _tracker.AvailableMonths;
        if (months.Count == 0) months = new List<DateTime> { _exMonth };
        if (!months.Contains(_exMonth)) _exMonth = months[0];
        var cb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = ContentW - 28, Font = new Font("Segoe UI", 9f), Location = new Point(14, 2) };
        foreach (var m in months) cb.Items.Add(m.ToString("MMMM yyyy", CultureInfo.CurrentCulture));
        cb.SelectedIndex = months.IndexOf(_exMonth);
        cb.SelectedIndexChanged += (_, _) => { _exMonth = months[cb.SelectedIndex]; Rebuild(); };
        row.Controls.Add(cb);
        return row;
    }

    private Control YearCombo()
    {
        var row = NewRow(30);
        var years = _tracker.AvailableYears;
        if (years.Count == 0) years = new List<int> { _exYear };
        if (!years.Contains(_exYear)) _exYear = years[0];
        var cb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = ContentW - 28, Font = new Font("Segoe UI", 9f), Location = new Point(14, 2) };
        foreach (var y in years) cb.Items.Add(y.ToString());
        cb.SelectedIndex = years.IndexOf(_exYear);
        cb.SelectedIndexChanged += (_, _) => { _exYear = years[cb.SelectedIndex]; Rebuild(); };
        row.Controls.Add(cb);
        return row;
    }

    private void ExportSave(DateInterval iv)
    {
        _suppressHide = true;
        try
        {
            using var dlg = new SaveFileDialog
            {
                FileName = ExDefaultName(),
                Filter = _exFormatCsv ? "CSV (*.csv)|*.csv" : "Excel (*.xlsx)|*.xlsx"
            };
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                if (_exFormatCsv)
                    // Csv() enthaelt bereits \uFEFF → Encoding ohne zusaetzliches BOM.
                    File.WriteAllText(dlg.FileName, _tracker.Csv(iv, _exDetailed), new UTF8Encoding(false));
                else
                    File.WriteAllBytes(dlg.FileName, _tracker.XlsxData(iv, _exDetailed));
            }
        }
        catch { /* Schreibfehler nicht fatal */ }
        finally { _suppressHide = false; }
    }

    private string ExDefaultName()
    {
        var ext = _exFormatCsv ? "csv" : "xlsx";
        return _exScopeIdx switch
        {
            0 => $"Zeiterfassung-{_exMonth:yyyy-MM}.{ext}",
            1 => $"Zeiterfassung-{_exYear}.{ext}",
            _ => $"Zeiterfassung-alle.{ext}"
        };
    }

    // MARK: Segmented / Stepper

    private Control Segmented(string[] opts, int sel, Action<int> onSel)
    {
        var row = NewRow(32);
        var bar = new Panel { Size = new Size(ContentW - 28, 26), Location = new Point(14, 3),
            BackColor = Color.FromArgb(237, 238, 241) };
        int segW = bar.Width / opts.Length;
        for (int i = 0; i < opts.Length; i++)
        {
            int idx = i;
            var seg = new Label
            {
                Text = opts[i], AutoSize = false, Size = new Size(segW - 4, 20),
                Location = new Point(2 + i * segW, 3), TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 9.5f, i == sel ? FontStyle.Bold : FontStyle.Regular),
                ForeColor = i == sel ? Theme.Ink : Theme.Sub,
                BackColor = i == sel ? Color.White : Color.Transparent, Cursor = Cursors.Hand
            };
            seg.Click += (_, _) => onSel(idx);
            bar.Controls.Add(seg);
        }
        row.Controls.Add(bar);
        return row;
    }

    private Control Stepper(string label, bool canForward, Action back, Action fwd)
    {
        var row = NewRow(28);
        var l = IconLabel(Glyphs.ChevronLeft, Theme.Ink, _iconFont); l.SetBounds(14, 4, 18, 20);
        l.Cursor = Cursors.Hand; l.Click += (_, _) => back();
        var mid = Text12(label, Theme.Ink, bold: true); mid.SetBounds(40, 6, ContentW - 80, 16);
        mid.TextAlign = ContentAlignment.MiddleCenter;
        var r = IconLabel(Glyphs.ChevronRight, canForward ? Theme.Ink : Theme.Tert, _iconFont);
        r.SetBounds(ContentW - 32, 4, 18, 20);
        if (canForward) { r.Cursor = Cursors.Hand; r.Click += (_, _) => fwd(); }
        row.Controls.Add(l); row.Controls.Add(mid); row.Controls.Add(r);
        return row;
    }

    // MARK: Einstellungen

    private void BuildEinstellungen()
    {
        _content.Controls.Add(SubHeader("Einstellungen"));
        _content.Controls.Add(Divider());

        _content.Controls.Add(ToggleRow(IconLabel(Glyphs.Wifi, Theme.Sub, _iconFont), "Auto-Start (WLAN)",
            _settings.AutoStartEnabled, enabled: true, on =>
            { _settings.AutoStartEnabled = on; _settings.Save(); _onAutoConfig(); Rebuild(); }));

        _content.Controls.Add(ToggleRow(IconLabel(Glyphs.Pause, Theme.Sub, _iconFont), "Auswärts stoppen",
            _settings.StopWhenAway, enabled: _settings.AutoStartEnabled, on =>
            { _settings.StopWhenAway = on; _settings.Save(); _onAutoConfig(); }));

        _content.Controls.Add(ToggleRow(IconLabel(Glyphs.Power, Theme.Sub, _iconFont), "Beim Login starten",
            LaunchAtLogin.IsEnabled, enabled: true, on => LaunchAtLogin.Set(on)));

        _content.Controls.Add(Divider());
        _content.Controls.Add(SectionLabel("ZEITKONTO"));

        _content.Controls.Add(NumericRow(IconLabel(Glyphs.Clock, Theme.Sub, _iconFont), "Wochenstunden",
            (decimal)_settings.Wochenstunden, 0m, 80m, 0.5m, v => { _settings.Wochenstunden = (double)v; _settings.Save(); }));

        _content.Controls.Add(NumericRow(PmIcon(), "Anfangssaldo (h)",
            (decimal)_settings.AnfangssaldoH, -999m, 999m, 0.5m, v => { _settings.AnfangssaldoH = (double)v; _settings.Save(); }));

        _content.Controls.Add(DateRow(IconLabel(Glyphs.Calendar, Theme.Sub, _iconFont), "Kontostart",
            _settings.KontostartOr(_tracker.EarliestStart), d => { _settings.KontostartStored = d; _settings.Save(); }));

        _content.Controls.Add(Divider());
        _content.Controls.Add(SectionLabel("WLAN-ZUORDNUNG"));

        _content.Controls.Add(AssignRow(IconLabel(Glyphs.Home, Theme.Sub, _iconFont), "Zuhause", _settings.HomeSsid,
            () => { _settings.HomeSsid = null; _settings.Save(); _onAutoConfig(); Rebuild(); }));
        _content.Controls.Add(AssignRow(IconLabel(Glyphs.Office, Theme.Sub, _iconFont), "Büro", _settings.OfficeSsid,
            () => { _settings.OfficeSsid = null; _settings.Save(); _onAutoConfig(); Rebuild(); }));
    }

    private Label PmIcon() => new()
    {
        Text = "\u00b1", AutoSize = false, Size = new Size(22, 20), ForeColor = Theme.Sub,
        Font = new Font("Segoe UI", 11f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter,
        BackColor = Color.Transparent
    };

    private Control GenericRow(Label icon, string title, Control trailing, int h = 30)
    {
        var row = NewRow(h);
        icon.SetBounds(12, (h - 18) / 2, 22, 18);
        var t = Text12(title, Theme.Ink); t.SetBounds(38, (h - 16) / 2, 150, 16);
        trailing.Location = new Point(ContentW - trailing.Width - 14, (h - trailing.Height) / 2);
        row.Controls.Add(icon); row.Controls.Add(t); row.Controls.Add(trailing);
        return row;
    }

    private Control ToggleRow(Label icon, string title, bool on, bool enabled, Action<bool> onToggle)
    {
        var sw = new ToggleSwitch { On = on, Enabled = enabled };
        sw.Toggled += (_, _) => onToggle(sw.On);
        return GenericRow(icon, title, sw, 28);
    }

    private Control NumericRow(Label icon, string title, decimal val, decimal min, decimal max, decimal step, Action<decimal> onChange)
    {
        var nud = new NumericUpDown
        {
            Minimum = min, Maximum = max, Increment = step, DecimalPlaces = 1,
            Width = 62, Font = new Font("Segoe UI", 9f), TextAlign = HorizontalAlignment.Right
        };
        nud.Value = Math.Clamp(val, min, max);
        nud.ValueChanged += (_, _) => onChange(nud.Value);
        return GenericRow(icon, title, nud, 30);
    }

    private Control DateRow(Label icon, string title, DateTime val, Action<DateTime> onChange)
    {
        var dp = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 110, Font = new Font("Segoe UI", 9f) };
        try { dp.Value = val; } catch { /* out of range → Default */ }
        dp.ValueChanged += (_, _) => onChange(dp.Value.Date);
        return GenericRow(icon, title, dp, 30);
    }

    private Control AssignRow(Label icon, string title, string? ssid, Action clear)
    {
        var trailing = new Panel { Size = new Size(130, 20), BackColor = Color.Transparent };
        var lbl = new Label
        {
            Text = ssid ?? "—", AutoSize = false, Size = new Size(ssid is null ? 126 : 110, 18),
            Location = new Point(0, 1), TextAlign = ContentAlignment.MiddleRight,
            ForeColor = ssid is null ? Theme.Tert : Theme.Sub, BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 9f)
        };
        trailing.Controls.Add(lbl);
        if (ssid is not null)
        {
            var x = new Label
            {
                Text = "\u00d7", AutoSize = false, Size = new Size(16, 18), Location = new Point(114, 0),
                TextAlign = ContentAlignment.MiddleCenter, ForeColor = Theme.Tert,
                Font = new Font("Segoe UI", 12f), Cursor = Cursors.Hand, BackColor = Color.Transparent
            };
            x.Click += (_, _) => clear();
            trailing.Controls.Add(x);
        }
        return GenericRow(icon, title, trailing, 26);
    }

    // MARK: Platzhalter (4)

    private void BuildPlaceholder(string title, string note)
    {
        _content.Controls.Add(SubHeader(title));
        _content.Controls.Add(Divider());
        var row = NewRow(60);
        var lbl = Text12(note, Theme.Tert);
        lbl.SetBounds(0, 20, ContentW, 20); lbl.TextAlign = ContentAlignment.MiddleCenter;
        row.Controls.Add(lbl);
        _content.Controls.Add(row);
    }

    // MARK: Bausteine

    private Panel NewRow(int height) => new()
    {
        Size = new Size(ContentW, height), Margin = new Padding(0), BackColor = Theme.Bg
    };

    private Panel Divider()
    {
        var p = new Panel { Size = new Size(ContentW - 28, 1), BackColor = Theme.Line, Margin = new Padding(14, 4, 14, 4) };
        return p;
    }

    private Label SectionLabel(string text)
    {
        var l = new Label
        {
            Text = text, AutoSize = false, Size = new Size(ContentW, 18),
            ForeColor = Theme.Tert, Font = new Font("Segoe UI", 8f, FontStyle.Bold),
            Padding = new Padding(14, 4, 0, 0), Margin = new Padding(0)
        };
        return l;
    }

    private Control SubHeader(string title)
    {
        var row = NewRow(32);
        var back = IconLabel(Glyphs.ChevronLeft, Theme.Ink, _iconFont); back.SetBounds(12, 6, 18, 20); back.Cursor = Cursors.Hand;
        var t = Text14(title, Theme.Ink, bold: true); t.SetBounds(32, 6, ContentW - 40, 20);
        back.Click += (_, _) => Navigate(Screen.Home);
        t.Click += (_, _) => Navigate(Screen.Home);
        row.Controls.Add(back); row.Controls.Add(t);
        return row;
    }

    /// <summary>Nicht-klickbare Statuszeile: Icon · Titel · Trailing.</summary>
    private Control StatusRow(Label icon, string title, Control trailing)
    {
        var row = NewRow(24);
        icon.SetBounds(12, 3, 22, 18);
        var t = Text12(title, Theme.Ink); t.SetBounds(38, 4, 120, 16);
        trailing.Location = new Point(ContentW - trailing.Width - 14, (24 - trailing.Height) / 2);
        row.Controls.Add(icon); row.Controls.Add(t); row.Controls.Add(trailing);
        return row;
    }

    /// <summary>Klickbare Aktionszeile (ganze Zeile trifft), optional Wert + Chevron.</summary>
    private Control ActionRow(string glyph, string title, Control? trailing = null,
        bool chevron = false, bool bold = false, Action? onClick = null)
    {
        var row = NewRow(26);
        var icon = IconLabel(glyph, Theme.Sub, _iconFont); icon.SetBounds(12, 5, 22, 18);
        var t = Text12(title, Theme.Ink, bold); t.SetBounds(38, 5, ContentW - 90, 16);
        row.Controls.Add(icon); row.Controls.Add(t);

        int rightEdge = ContentW - 14;
        if (chevron)
        {
            var ch = IconLabel(Glyphs.ChevronRight, Theme.Tert, _iconSmall);
            ch.SetBounds(ContentW - 22, 6, 16, 16);
            row.Controls.Add(ch);
            rightEdge = ContentW - 26;
        }
        if (trailing is not null)
        {
            trailing.Location = new Point(rightEdge - trailing.Width, (26 - trailing.Height) / 2);
            row.Controls.Add(trailing);
        }

        if (onClick is not null)
        {
            Clickable(row, onClick);
            row.MouseEnter += (_, _) => SetRowBg(row, Theme.Hover);
            row.MouseLeave += (_, _) => SetRowBg(row, Theme.Bg);
        }
        return row;
    }

    private static void SetRowBg(Control row, Color c)
    {
        row.BackColor = c;
        foreach (Control ch in row.Controls)
            if (ch is Label) ch.BackColor = Color.Transparent;
    }

    /// <summary>Haengt den Klick an die Zeile und alle (nicht interaktiven) Kinder.</summary>
    private static void Clickable(Control root, Action onClick)
    {
        root.Cursor = Cursors.Hand;
        root.Click += (_, _) => onClick();
        foreach (Control c in root.Controls)
        {
            if (c is Button || c is DateTimePicker) continue; // eigene Interaktion
            c.Cursor = Cursors.Hand;
            c.Click += (_, _) => onClick();
        }
    }

    private Label IconLabel(string glyph, Color color, Font font) => new()
    {
        Text = glyph, Font = font, ForeColor = color, AutoSize = false,
        Size = new Size(22, 20), TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.Transparent
    };

    private Label Text11(string s, Color c) => MakeLabel(s, c, 9f, false);
    private Label Text12(string s, Color c, bool bold = false) => MakeLabel(s, c, 9.5f, bold);
    private Label Text14(string s, Color c, bool bold = false) => MakeLabel(s, c, 11f, bold);

    private Label MakeLabel(string s, Color c, float size, bool bold)
    {
        var l = new Label
        {
            Text = s, ForeColor = c, AutoSize = false, BackColor = Color.Transparent,
            Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
            TextAlign = ContentAlignment.MiddleLeft, Size = new Size(100, 18)
        };
        return l;
    }

    private static void MakeRound(Control c)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        path.AddEllipse(0, 0, c.Width, c.Height);
        c.Region = new Region(path);
    }
}

internal static class LabelExt
{
    public static void SetTextIfChanged(this Label l, string s) { if (l.Text != s) l.Text = s; }
}

/// <summary>Custom-gezeichneter Pill-Schalter (WinForms hat keinen nativen Toggle).</summary>
internal sealed class ToggleSwitch : Control
{
    private bool _on;
    public bool On { get => _on; set { _on = value; Invalidate(); } }
    public event EventHandler? Toggled;

    public ToggleSwitch()
    {
        Size = new Size(38, 20);
        Cursor = Cursors.Hand;
        DoubleBuffered = true;
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    protected override void OnClick(EventArgs e)
    {
        if (!Enabled) return;
        base.OnClick(e);
        _on = !_on;
        Invalidate();
        Toggled?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        float s = DeviceDpi / 96f;   // selbstgezeichnet -> eigene DPI-Skalierung
        int h = (int)(18 * s), w = (int)(36 * s), y = (Height - h) / 2;
        var track = new Rectangle(0, y, w, h);
        var col = !Enabled ? Color.FromArgb(230, 232, 236) : (_on ? Theme.Accent : Theme.TrackOff);
        using (var b = new SolidBrush(col))
        using (var path = Rounded(track, h / 2))
            g.FillPath(b, path);

        int d = h - (int)(4 * s);
        int kx = _on ? track.Right - d - (int)(2 * s) : track.Left + (int)(2 * s);
        using var kb = new SolidBrush(Color.White);
        g.FillEllipse(kb, kx, track.Top + (int)(2 * s), d, d);
    }

    private static GraphicsPath Rounded(Rectangle r, int rad)
    {
        var p = new GraphicsPath();
        int dia = rad * 2;
        p.AddArc(r.X, r.Y, dia, dia, 90, 180);
        p.AddArc(r.Right - dia, r.Y, dia, dia, 270, 180);
        p.CloseFigure();
        return p;
    }
}
