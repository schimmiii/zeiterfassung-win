using System.Text.Json;

namespace Zeiterfassung;

/// <summary>
/// Persistente Einstellungen in %APPDATA%\Zeiterfassung\settings.json.
/// Defaults an den Mac-Stand angeglichen: Auto-Start an, Auswaerts-Stopp an.
/// Zeitkonto (Modell B): Wochenstunden, Anfangssaldo, Kontostart.
/// </summary>
public sealed class Settings
{
    // Auto-Start per WLAN — Default AN (wie Mac).
    public bool AutoStartEnabled { get; set; } = true;
    // Bei unbekanntem Netz stoppen — Default AN (wie Mac).
    public bool StopWhenAway { get; set; } = true;
    // Auto-Start nur Mo–Fr — Default AN (wie Mac). Verhindert Wochenend-Erfassung.
    public bool AutoStartWorkdaysOnly { get; set; } = true;

    public string? HomeSsid { get; set; }
    public string? OfficeSsid { get; set; }

    // Zeitkonto
    public double Wochenstunden { get; set; } = 40;
    public double AnfangssaldoH { get; set; }
    /// <summary>Persistierter Kontostart; null → frühestes Segment (vom Aufrufer aufgeloest).</summary>
    public DateTime? KontostartStored { get; set; }

    // Push 4: LaunchAtLogin (via Registry Run-Key)

    /// <summary>Effektiver Kontostart: gesetzt → gespeichert, sonst frühestes Segment, sonst heute.</summary>
    public DateTime KontostartOr(DateTime? earliest) =>
        KontostartStored ?? earliest ?? DateTime.Now;

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
            return Path.Combine(dir, "settings.json");
        }
    }

    public static Settings Load()
    {
        try
        {
            var path = FilePath;
            if (!File.Exists(path)) return new Settings();
            return JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), JsonOpts) ?? new Settings();
        }
        catch
        {
            return new Settings();
        }
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(this, JsonOpts);
            var path = FilePath;
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }
        catch
        {
            // Persistenz-Fehler nicht fatal.
        }
    }
}
