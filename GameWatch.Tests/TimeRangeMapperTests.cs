using GameWatch.Services;
using Xunit;

namespace GameWatch.Tests;

public class TimeRangeMapperTests
{
    private static readonly DateTime Base = new(2026, 9, 30, 10, 0, 0);

    [Fact]
    public void PinnedEndFollowsNewest()
    {
        var range = new TimeRangeMapper();
        range.SetBounds(Base, Base.AddMinutes(10));
        range.DragStart(-100, 200);
        range.SetBounds(Base, Base.AddMinutes(12));
        Assert.Equal(Base.AddMinutes(12), range.End);
    }

    [Fact]
    public void PinnedStartFollowsOldestWhenTrimmed()
    {
        var range = new TimeRangeMapper();
        range.SetBounds(Base, Base.AddMinutes(10));
        range.Preset(null);
        range.DragEnd(-50, 200);
        range.SetBounds(Base.AddMinutes(2), Base.AddMinutes(12));
        Assert.Equal(Base.AddMinutes(2), range.Start);
    }

    [Fact]
    public void HandlesCannotCrossAndReachBothEdges()
    {
        var range = new TimeRangeMapper();
        range.SetBounds(Base, Base.AddMinutes(10));
        range.DragStart(-1000, 200);
        range.DragEnd(-1000, 200);
        Assert.Equal(Base, range.Start);
        Assert.Equal(Base, range.End);
        range.DragEnd(1000, 200);
        range.DragStart(1000, 200);
        Assert.Equal(Base.AddMinutes(10), range.Start);
        Assert.Equal(Base.AddMinutes(10), range.End);
    }

    [Fact]
    public void PresetsFollowTheLatestSamples()
    {
        var range = new TimeRangeMapper();
        range.SetBounds(Base, Base.AddHours(2));
        range.Preset(TimeSpan.FromMinutes(15));
        range.SetBounds(Base, Base.AddHours(3));
        Assert.Equal(Base.AddHours(3).AddMinutes(-15), range.Start);
        range.Preset(null);
        Assert.Equal(Base, range.Start);
    }
}
