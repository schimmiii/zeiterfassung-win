using System.Text.Json.Serialization;

namespace Zeiterfassung;

/// <summary>
/// Ein Arbeitsabschnitt. <c>End == null</c> bedeutet: laeuft gerade.
/// Portiert aus WorkSegment (Swift). Zeiten in lokaler Zeit als DateTime.
/// </summary>
public sealed class WorkSegment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime Start { get; set; }
    public DateTime? End { get; set; }

    public WorkSegment() { }

    public WorkSegment(DateTime start, DateTime? end)
    {
        Start = start;
        End = end;
    }

    [JsonIgnore]
    public bool IsRunning => End is null;

    /// <summary>
    /// Anteil der Dauer, der in <paramref name="interval"/> faellt
    /// (fuer Tages-/Wochensummen). Offenes Segment nutzt <paramref name="now"/> als Ende.
    /// </summary>
    public TimeSpan DurationIn(DateInterval interval, DateTime now)
    {
        var e = End ?? now;
        var lo = Start > interval.Start ? Start : interval.Start;
        var hi = e < interval.End ? e : interval.End;
        var d = hi - lo;
        return d > TimeSpan.Zero ? d : TimeSpan.Zero;
    }
}

/// <summary>Halb-offenes Zeitintervall [Start, End). Ersatz fuer Swifts DateInterval.</summary>
public readonly record struct DateInterval(DateTime Start, DateTime End);
