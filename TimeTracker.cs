using System.Globalization;
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

    /// <summary>Segmente, die den heutigen Tag beruehren — sortiert, fuers Nachtragen-Panel.</summary>
    public List<WorkSegment> TodaySegments()
    {
        var now = DateTime.Now;
        var iv = DayInterval(now);
        return _segments
            .Where(s => (s.End ?? now) > iv.Start && s.Start < iv.End)
            .OrderBy(s => s.Start)
            .ToList();
    }

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
}
