using GameWatch.Services;
using Xunit;

namespace GameWatch.Tests;

public class RateFormattingTests
{
    [Theory]
    [InlineData(0, "0 B/s")]
    [InlineData(512, "512 B/s")]
    [InlineData(2048, "2.0 KB/s")]
    [InlineData(5 * 1024 * 1024, "5.00 MB/s")]
    public void FormatsRateWithCorrectUnit(double bytesPerSecond, string expected)
    {
        Assert.Equal(expected, RateFormatting.FormatRate(bytesPerSecond));
    }

    [Theory]
    [InlineData(0, RateLevel.Normal)]
    [InlineData(500 * 1024, RateLevel.Normal)]
    [InlineData(2 * 1024 * 1024, RateLevel.Elevated)]
    [InlineData(6 * 1024 * 1024, RateLevel.High)]
    public void ClassifiesRateLevelByThreshold(double bytesPerSecond, RateLevel expected)
    {
        Assert.Equal(expected, RateFormatting.GetRateLevel(bytesPerSecond));
    }
}
