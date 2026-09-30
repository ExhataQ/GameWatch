using System.IO;
using System.Text.Json;

namespace GameWatch.Services;

public enum RefreshSpeed { Low, Normal, Fast }

public enum AdapterFilterMode { Automatic, Ethernet, WiFi, VpnOrVirtual }

// Persisted user configuration - refresh cadence, adapter filter, Game
// Mode target lists, and the optional bandwidth alert threshold. Kept as
// a single flat JSON file rather than pulling in a configuration
// framework, per the "use existing project conventions" guidance.
public class AppSettings
{
    public RefreshSpeed UiRefreshSpeed { get; set; } = RefreshSpeed.Normal;
    public AdapterFilterMode AdapterFilter { get; set; } = AdapterFilterMode.Automatic;
    public List<string> GameModeServiceNames { get; set; } = new() { "BITS", "DoSvc", "wuauserv" };
    public List<string> GameModeProcessNames { get; set; } = new();

    // MB/s. 0 disables the alert entirely.
    public double BandwidthAlertThresholdMBps { get; set; } = 0;

    public static TimeSpan IntervalFor(RefreshSpeed speed) => speed switch
    {
        RefreshSpeed.Low => TimeSpan.FromSeconds(5),
        RefreshSpeed.Fast => TimeSpan.FromSeconds(1),
        _ => TimeSpan.FromSeconds(2)
    };

    private static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GameWatch", "settings.json");

    public static AppSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (!File.Exists(path)) return new AppSettings();
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            // A corrupt or unreadable settings file shouldn't block
            // startup - fall back to defaults rather than crash.
            return new AppSettings();
        }
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch
        {
            // Best-effort; a failed save shouldn't crash the app.
        }
    }
}
