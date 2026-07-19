using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Zeiterfassung;

/// <summary>
/// Echte Zeiterfassung: Start/Stop schreibt Segmente, persistiert als JSON in
/// %APPDATA%\Zeiterfassung\segments.json. Tages-/Wochensumme live berechnet.
/// Ein beim Beenden offenes Segment laeuft nach Neustart weiter.
/// Portiert aus TimeTracker (Swift).
/// </summary>
public sealed class TimeTracker
{
    private readonly List<WorkSegment> _segments = new();

    /// <summary>Feuert bei jeder Zustandsaenderung (Start/Stop/Nachtrag/Loeschen).</summary>
    public event Action? Changed;

    public TimeTracker()
    {
        Load();
    }

    public IReadOnlyList<WorkSegment> Segments => _segments;

    // Reihenfolge-robust: nicht auf Last verlassen, sondern das offene Segment suchen.
    public bool IsRunning => _segments.Any(s => s.End is null);
    public DateTime? RunningSince => _segments.FirstOrDefault(s => s.End is null)?.Start;

    public void Start()
    {
        if (IsRunning) return;
        _segments.Add(new WorkSegment(DateTime.Now, null));
        Save();
        Changed?.Invoke();
    }

    public void Stop()
    {
        var seg = _segments.FirstOrDefault(s => s.End is null);
        if (seg is null) return;
        seg.End = DateTime.Now;
        Save();
        Changed?.Invoke();
    }

    public void Toggle()
    {
        if (IsRunning) Stop(); else Start();
    }

    // MARK: Sleep/Lock — pausiert bei Sleep/Bildschirmsperre, laeuft bei Wake/Unlock weiter.
    // Die OS-Events abonniert die Host-Schicht (TrayAppContext) und ruft diese Methoden.

    private bool _wasRunningBeforePause;

    /// <summary>Pausiert nur, wenn wirklich gerade laeuft — Flag bleibt bei Sleep UND Lock erhalten.</summary>
    public void PauseForSystem()
    {
        if (!IsRunning) return;
        _wasRunningBeforePause = true;
        Stop();
    }

    public void ResumeFromSystem()
    {
        if (!_wasRunningBeforePause) return;
        _wasRunningBeforePause = false;
        Start();
    }

    // MARK: Nachtragen / Korrektur

    /// <summary>Fuegt ein manuelles, abgeschlossenes Segment ein. Ignoriert leere/negative Spannen.</summary>
    public void AddManual(DateTime start, DateTime end)
    {
        if (end <= start) return;
        _segments.Add(new WorkSegment(start, end));
        Save();
        Changed?.Invoke();
    }

    public void Delete(Guid id)
    {
        var removed = _segments.RemoveAll(s => s.Id == id);
        if (removed == 0) return;
        Save();
        Changed?.Invoke();
    }

    /// <summary>Segmente, die einen bestimmten Kalendertag beruehren — sortiert, fuers Nachtragen-Panel.</summary>
    public List<WorkSegment> DaySegmentsFor(DateTime day)
    {
        var now = DateTime.Now;
        var iv = DayInterval(day);
        return _segments
            .Where(s => (s.End ?? now) > iv.Start && s.Start < iv.End)
            .OrderBy(s => s.Start)
            .ToList();
    }

    /// <summary>Segmente, die den heutigen Tag beruehren.</summary>
    public List<WorkSegment> TodaySegments() => DaySegmentsFor(DateTime.Now);

    // MARK: Summen

    public TimeSpan TodayTotal()
    {
        var now = DateTime.Now;
        var iv = DayInterval(now);
        return _segments.Aggregate(TimeSpan.Zero, (acc, s) => acc + s.DurationIn(iv, now));
    }

    public TimeSpan WeekTotal()
    {
        var now = DateTime.Now;
        var iv = WeekInterval(now);
        return _segments.Aggregate(TimeSpan.Zero, (acc, s) => acc + s.DurationIn(iv, now));
    }

    public readonly record struct DayTotal(DateTime Date, TimeSpan Total);

    /// <summary>Summen pro Tag der laufenden Woche (Wochenstart Locale-abhaengig).</summary>
    public List<DayTotal> WeekBreakdown()
    {
        var now = DateTime.Now;
        var week = WeekInterval(now);
        var result = new List<DayTotal>();
        var day = week.Start;
        while (day < week.End)
        {
            var next = day.AddDays(1);
            var iv = new DateInterval(day, next);
            var total = _segments.Aggregate(TimeSpan.Zero, (acc, s) => acc + s.DurationIn(iv, now));
            result.Add(new DayTotal(day, total));
            day = next;
        }
        return result;
    }

    // MARK: Beliebige Intervalle / Zeitkonto

    /// <summary>Summe aller Segmente in einem beliebigen Intervall.</summary>
    public TimeSpan Total(DateInterval interval)
    {
        var now = DateTime.Now;
        return _segments.Aggregate(TimeSpan.Zero, (acc, s) => acc + s.DurationIn(interval, now));
    }

    /// <summary>Fruehester erfasster Zeitpunkt (fuer Kontostart-Default / "Alles"-Export).</summary>
    public DateTime? EarliestStart =>
        _segments.Count == 0 ? null : _segments.Min(s => s.Start);

    /// <summary>
    /// Kumulierter Saldo (vorzeichenbehaftet) nach Modell B (Werktags-Konto), 1:1 aus Swift:
    /// abgeschlossene Werktage Mo–Fr MIT erfasster Zeit (ab Kontostart bis GESTERN) gehen mit
    /// Ist − Tages-Soll ein; Wochenende zaehlt nicht; heute nur als Ist-Plus (Soll erst ab morgen).
    /// Selbst-heilend gegenueber Urlaub/Downtime.
    /// </summary>
    public TimeSpan SaldoKumuliert(double wochenstunden, double anfangssaldoH, DateTime kontostart)
    {
        var now = DateTime.Now;
        var sollSec = wochenstunden / 5.0 * 3600.0;
        var today = now.Date;
        var saldo = anfangssaldoH * 3600.0;

        var day = kontostart.Date;
        while (day < today) // nur abgeschlossene Tage bekommen ein Soll
        {
            if (day.DayOfWeek != DayOfWeek.Saturday && day.DayOfWeek != DayOfWeek.Sunday)
            {
                var next = day.AddDays(1);
                var ist = Total(new DateInterval(day, next)).TotalSeconds;
                if (ist > 0) saldo += ist - sollSec;
            }
            day = day.AddDays(1);
        }
        // Heute: reines Ist-Plus, kein Soll-Abzug.
        saldo += Total(new DateInterval(today, today.AddDays(1))).TotalSeconds;
        return TimeSpan.FromSeconds(saldo);
    }

    // MARK: Monats-/Jahresaufschluesselung (Uebersicht)

    public readonly record struct PeriodTotal(string Label, DateTime Date, TimeSpan Total);

    /// <summary>Wochen-Summen (KW) innerhalb eines Monats; Nachbarmonats-Tage per Schnittmenge Woche∩Monat abgeschnitten.</summary>
    public List<PeriodTotal> MonthBreakdown(DateTime month)
    {
        var cal = CultureInfo.CurrentCulture.Calendar;
        var monthStart = new DateTime(month.Year, month.Month, 1);
        var monthEnd = monthStart.AddMonths(1);
        var result = new List<PeriodTotal>();

        var weekStart = StartOfWeek(monthStart);
        while (weekStart < monthEnd)
        {
            var weekEnd = weekStart.AddDays(7);
            var lo = weekStart > monthStart ? weekStart : monthStart;
            var hi = weekEnd < monthEnd ? weekEnd : monthEnd;
            var kw = ISOWeek.GetWeekOfYear(weekStart);
            result.Add(new PeriodTotal($"KW {kw}", weekStart, Total(new DateInterval(lo, hi))));
            weekStart = weekEnd;
        }
        return result;
    }

    /// <summary>Monats-Summen innerhalb eines Jahres (Jan–Dez).</summary>
    public List<PeriodTotal> YearBreakdown(int year)
    {
        var de = CultureInfo.CurrentCulture;
        return Enumerable.Range(1, 12).Select(m =>
        {
            var start = new DateTime(year, m, 1);
            var iv = new DateInterval(start, start.AddMonths(1));
            return new PeriodTotal(start.ToString("MMM", de), start, Total(iv));
        }).ToList();
    }

    // MARK: Export

    /// <summary>Segmente, deren Start in <paramref name="interval"/> faellt — sortiert.</summary>
    public List<WorkSegment> SegmentsIn(DateInterval interval) =>
        _segments.Where(s => s.Start >= interval.Start && s.Start < interval.End)
                 .OrderBy(s => s.Start).ToList();

    /// <summary>Vorhandene Monate (erster Tag), absteigend.</summary>
    public List<DateTime> AvailableMonths =>
        _segments.Select(s => new DateTime(s.Start.Year, s.Start.Month, 1))
                 .Distinct().OrderByDescending(d => d).ToList();

    /// <summary>Vorhandene Jahre, absteigend.</summary>
    public List<int> AvailableYears =>
        _segments.Select(s => s.Start.Year).Distinct().OrderByDescending(y => y).ToList();

    public abstract record ExportCell
    {
        public sealed record Text(string Value) : ExportCell;
        public sealed record Number(double Value) : ExportCell;
    }

    /// <summary>
    /// Export-Matrix inkl. Kopf- und Summenzeile.
    /// Default (detailed=false): 1 Zeile pro Tag (Datum; Dauer_h).
    /// detailed=true: 1 Zeile pro Segment (Datum; Von; Bis; Dauer_h).
    /// </summary>
    public List<List<ExportCell>> ExportMatrix(DateInterval interval, bool detailed)
    {
        var now = DateTime.Now;
        var segs = SegmentsIn(interval);
        var rows = new List<List<ExportCell>>();
        double sum = 0;

        if (detailed)
        {
            rows.Add(new() { new ExportCell.Text("Datum"), new ExportCell.Text("Von"),
                             new ExportCell.Text("Bis"), new ExportCell.Text("Dauer_h") });
            foreach (var s in segs)
            {
                var end = s.End ?? now;
                var dur = Math.Max(0, (end - s.Start).TotalSeconds);
                sum += dur;
                rows.Add(new() {
                    new ExportCell.Text(s.Start.ToString("dd.MM.yyyy")),
                    new ExportCell.Text(s.Start.ToString("HH:mm")),
                    new ExportCell.Text(end.ToString("HH:mm")),
                    new ExportCell.Number(dur / 3600.0) });
            }
            rows.Add(new() { new ExportCell.Text("Summe"), new ExportCell.Text(""),
                             new ExportCell.Text(""), new ExportCell.Number(sum / 3600.0) });
        }
        else
        {
            rows.Add(new() { new ExportCell.Text("Datum"), new ExportCell.Text("Dauer_h") });
            var byDay = new SortedDictionary<DateTime, double>();
            foreach (var s in segs)
            {
                var end = s.End ?? now;
                var day = s.Start.Date;
                byDay[day] = byDay.GetValueOrDefault(day) + Math.Max(0, (end - s.Start).TotalSeconds);
            }
            foreach (var kv in byDay)
            {
                sum += kv.Value;
                rows.Add(new() { new ExportCell.Text(kv.Key.ToString("dd.MM.yyyy")),
                                 new ExportCell.Number(kv.Value / 3600.0) });
            }
            rows.Add(new() { new ExportCell.Text("Summe"), new ExportCell.Number(sum / 3600.0) });
        }
        return rows;
    }

    /// <summary>CSV im dt. Excel-Format: Semikolon, Komma-Dezimal, TT.MM.JJJJ, UTF-8-BOM.</summary>
    public string Csv(DateInterval interval, bool detailed)
    {
        string Fmt(ExportCell c) => c switch
        {
            ExportCell.Text t => t.Value,
            ExportCell.Number n => n.Value.ToString("F2", CultureInfo.InvariantCulture).Replace('.', ','),
            _ => ""
        };
        var lines = ExportMatrix(interval, detailed).Select(r => string.Join(";", r.Select(Fmt)));
        return "\uFEFF" + string.Join("\n", lines) + "\n";
    }

    /// <summary>xlsx (OOXML-ZIP) ohne Dependency — Parts via <see cref="ZipArchive"/> nativ gepackt.</summary>
    public byte[] XlsxData(DateInterval interval, bool detailed)
    {
        var rows = ExportMatrix(interval, detailed);

        static string Esc(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        static string Col(int i)
        {
            var n = i; var s = "";
            do { s = (char)('A' + n % 26) + s; n = n / 26 - 1; } while (n >= 0);
            return s;
        }

        var sheet = new StringBuilder();
        sheet.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sheet.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
        for (int r = 0; r < rows.Count; r++)
        {
            sheet.Append($"<row r=\"{r + 1}\">");
            for (int c = 0; c < rows[r].Count; c++)
            {
                var reff = $"{Col(c)}{r + 1}";
                switch (rows[r][c])
                {
                    case ExportCell.Text t:
                        sheet.Append($"<c r=\"{reff}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{Esc(t.Value)}</t></is></c>");
                        break;
                    case ExportCell.Number n:
                        sheet.Append($"<c r=\"{reff}\" t=\"n\"><v>{n.Value.ToString("F2", CultureInfo.InvariantCulture)}</v></c>");
                        break;
                }
            }
            sheet.Append("</row>");
        }
        sheet.Append("</sheetData></worksheet>");

        const string contentTypes = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
            + "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
            + "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>"
            + "<Default Extension=\"xml\" ContentType=\"application/xml\"/>"
            + "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>"
            + "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>"
            + "</Types>";
        const string relsRoot = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
            + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
            + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>"
            + "</Relationships>";
        const string workbook = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
            + "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">"
            + "<sheets><sheet name=\"Zeiterfassung\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>";
        const string workbookRels = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
            + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
            + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>"
            + "</Relationships>";

        var enc = new UTF8Encoding(false); // OOXML-Parts ohne BOM
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string path, string content)
            {
                var e = zip.CreateEntry(path, CompressionLevel.Optimal);
                using var w = new StreamWriter(e.Open(), enc);
                w.Write(content);
            }
            Add("[Content_Types].xml", contentTypes);
            Add("_rels/.rels", relsRoot);
            Add("xl/workbook.xml", workbook);
            Add("xl/_rels/workbook.xml.rels", workbookRels);
            Add("xl/worksheets/sheet1.xml", sheet.ToString());
        }
        return ms.ToArray();
    }

    private static DateTime StartOfWeek(DateTime d)
    {
        var fdow = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        int diff = ((int)d.DayOfWeek - (int)fdow + 7) % 7;
        return d.Date.AddDays(-diff);
    }

    // MARK: Kalender-Intervalle

    private static DateInterval DayInterval(DateTime date)
    {
        var start = date.Date;
        return new DateInterval(start, start.AddDays(1));
    }

    private static DateInterval WeekInterval(DateTime date)
    {
        // Wochenstart nach aktueller Kultur (DE = Montag).
        var fdow = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        int diff = ((int)date.DayOfWeek - (int)fdow + 7) % 7;
        var start = date.Date.AddDays(-diff);
        return new DateInterval(start, start.AddDays(7));
    }

    // MARK: Persistenz (JSON in %APPDATA%\Zeiterfassung)

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static string FilePath
    {
        get
        {
            var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(baseDir, "Zeiterfassung");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "segments.json");
        }
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_segments, JsonOpts);
            var path = FilePath;
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, json);
            // Atomar ersetzen (kein halbgeschriebenes File bei Absturz).
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }
        catch
        {
            // Persistenz-Fehler darf die App nicht abschiessen; Zustand bleibt im RAM.
        }
    }

    private void Load()
    {
        try
        {
            var path = FilePath;
            if (!File.Exists(path)) return;
            var json = File.ReadAllText(path);
            var decoded = JsonSerializer.Deserialize<List<WorkSegment>>(json, JsonOpts);
            if (decoded is null) return;
            _segments.Clear();
            foreach (var s in decoded)
            {
                if (s.Id == Guid.Empty) s.Id = Guid.NewGuid(); // tolerantes Decoding
                _segments.Add(s);
            }
        }
        catch
        {
            // Kaputte Datei -> leer starten statt crashen.
        }
    }
}

/// <summary>„Hh MMmin" — fuer Tooltip und Menue.</summary>
public static class Format
{
    public static string Hhmm(TimeSpan ts)
    {
        var total = (int)ts.TotalSeconds;
        if (total < 0) total = 0;
        return $"{total / 3600}h {(total % 3600) / 60:00}min";
    }

    /// <summary>„+4h 12min" / „−2h 15min" — vorzeichenbehaftet fuer den Saldo.</summary>
    public static string Signed(TimeSpan ts)
    {
        var sign = ts.Ticks < 0 ? "\u2212" : "+"; // echtes Minus-Zeichen
        return sign + Hhmm(ts.Duration());
    }
}
