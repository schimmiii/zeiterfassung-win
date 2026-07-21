using System.Text.Json.Serialization;

namespace Zeiterfassung;

/// <summary>
/// Art einer ganztaegigen Abwesenheit. Fuer den Saldo sind alle Typen identisch
/// (der Werktag wird uebersprungen); sie unterscheiden sich nur in Anzeige/Zaehlung.
/// Portiert aus AbsenceType (Swift). Als String serialisiert (urlaub/krank/feiertag).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AbsenceType { Urlaub, Krank, Feiertag }

/// <summary>Ein ganzer Abwesenheitstag (Datum auf Mitternacht normalisiert).</summary>
public sealed class AbsenceDay
{
    public DateTime Date { get; set; }
    public AbsenceType Type { get; set; }

    public AbsenceDay() { }
    public AbsenceDay(DateTime date, AbsenceType type) { Date = date; Type = type; }
}

/// <summary>Ein zusammenhaengender Block gleichen Typs (fuer die Liste im Screen).</summary>
public readonly record struct AbsenceRun(DateTime Start, DateTime End, AbsenceType Type, int Workdays);

/// <summary>Label und Emoji je Typ — zentral, damit UI und Zaehlung konsistent bleiben.</summary>
public static class AbsenceInfo
{
    public static string Label(this AbsenceType t) => t switch
    {
        AbsenceType.Urlaub => "Urlaub",
        AbsenceType.Krank => "Krank",
        AbsenceType.Feiertag => "Feiertag",
        _ => t.ToString()
    };

    public static string Emoji(this AbsenceType t) => t switch
    {
        AbsenceType.Urlaub => "\U0001F3D6",   // 🏖
        AbsenceType.Krank => "\U0001F912",    // 🤒
        AbsenceType.Feiertag => "\U0001F38C", // 🎌
        _ => ""
    };
}
