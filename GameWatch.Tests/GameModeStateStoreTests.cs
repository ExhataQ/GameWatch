using System.IO;
using GameWatch.Services;
using Xunit;

namespace GameWatch.Tests;

public class GameModeStateStoreTests : IDisposable
{
    private readonly string _tempPath = Path.Combine(Path.GetTempPath(), $"gamewatch-test-{Guid.NewGuid()}.json");

    public void Dispose()
    {
        if (File.Exists(_tempPath)) File.Delete(_tempPath);
    }

    [Fact]
    public void LoadReturnsNullWhenNoFileExists()
    {
        Assert.Null(GameModeStateStore.Load(_tempPath));
    }

    [Fact]
    public void SaveThenLoadRoundTripsState()
    {
        var state = new GameModeState(new List<string> { "BITS", "DoSvc" }, new List<int> { 111, 222 }, DateTime.Now);

        GameModeStateStore.Save(state, _tempPath);
        var loaded = GameModeStateStore.Load(_tempPath);

        Assert.NotNull(loaded);
        Assert.Equal(state.StoppedServices, loaded!.StoppedServices);
        Assert.Equal(state.PausedProcessIds, loaded.PausedProcessIds);
    }

    [Fact]
    public void ClearRemovesTheFile()
    {
        GameModeStateStore.Save(new GameModeState(new List<string>(), new List<int>(), DateTime.Now), _tempPath);

        GameModeStateStore.Clear(_tempPath);

        Assert.Null(GameModeStateStore.Load(_tempPath));
    }

    [Fact]
    public void LoadReturnsNullForCorruptFile()
    {
        File.WriteAllText(_tempPath, "{ not valid json");

        Assert.Null(GameModeStateStore.Load(_tempPath));
    }
}
