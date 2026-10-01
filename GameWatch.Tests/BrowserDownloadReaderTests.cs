using GameWatch.Services;
using Xunit;

namespace GameWatch.Tests;

public class BrowserDownloadReaderTests
{
    [Theory]
    [InlineData("setup.exe.crdownload")]
    [InlineData("movie.part")]
    [InlineData("archive.partial")]
    [InlineData("document.opdownload")]
    public void RecognizesPartialDownloads(string filename) => Assert.True(BrowserDownloadReader.IsPartialFile(filename));

    [Fact]
    public void IgnoresCompletedFiles() => Assert.False(BrowserDownloadReader.IsPartialFile("setup.exe"));
}
