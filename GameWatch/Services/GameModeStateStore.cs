using System.IO;
using System.Text.Json;

namespace GameWatch.Services;

// What Game Mode actually changed, persisted to disk immediately when
// Enable() finishes and cleared immediately when Disable() finishes. If
// the app crashes or is killed while this file still exists, the next
// launch knows exactly what to restore instead of leaving services
// stopped or processes suspended indefinitely.
public record GameModeState(List<string> StoppedServices, List<int> PausedProcessIds, DateTime EnabledAt);

public static class GameModeStateStore
{
    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GameWatch", "gamemode-state.json");

    public static void Save(GameModeState state, string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonSerializer.Serialize(state));
        }
        catch
        {
            // Best-effort - if we can't persist it, Disable() during this
            // same run will still restore things normally.
        }
    }

    public static GameModeState? Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<GameModeState>(File.ReadAllText(path));
        }
        catch
        {
            // Corrupt leftover file - nothing useful to restore from it.
            return null;
        }
    }

    public static void Clear(string? path = null)
    {
        path ??= DefaultPath;
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
