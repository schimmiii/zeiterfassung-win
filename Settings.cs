using System.Text.Json;

namespace Zeiterfassung;

/// <summary>
/// Persistente Einstellungen in %APPDATA%\Zeiterfassung\settings.json.
/// Push 2: Auto-Start per WLAN. Push 4 ergaenzt LaunchAtLogin etc.
/// </summary>
public sealed class Settings
{
    public bool AutoStartEnabled { get; set; }
    public bool StopWhenAway { get; set; }
    public string? HomeSsid { get; set; }
    public string? OfficeSsid { get; set; }

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
