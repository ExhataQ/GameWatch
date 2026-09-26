namespace GameWatch.Services;

public enum RateLevel { Normal, Elevated, High }

// Pure, OS-independent formatting/classification - split out of MainWindow
// so it can be unit tested without a WPF or Windows environment.
public static class RateFormatting
{
    public static string FormatRate(double bytesPerSecond)
    {
        if (bytesPerSecond >= 1024 * 1024) return $"{bytesPerSecond / (1024 * 1024):N2} MB/s";
        if (bytesPerSecond >= 1024) return $"{bytesPerSecond / 1024:N1} KB/s";
        return $"{bytesPerSecond:N0} B/s";
    }

    public static RateLevel GetRateLevel(double bytesPerSecond)
    {
        var mbps = bytesPerSecond / (1024 * 1024);
        if (mbps >= 5) return RateLevel.High;
        if (mbps >= 1) return RateLevel.Elevated;
        return RateLevel.Normal;
    }
}
