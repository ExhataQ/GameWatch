using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace GameWatch.Services;

public record BitsTransfer(string Job, string State, string Source, string Destination, long Transferred, long Total)
{
    public string ProgressDisplay => $"{ByteFormatting.FormatBytes(Transferred)} / {(Total > 0 ? ByteFormatting.FormatBytes(Total) : "unknown")}";
}

public static class BitsTransferReader
{
    public static async Task<IReadOnlyList<BitsTransfer>> ReadAsync()
    {
        const string script = "$ErrorActionPreference='Stop'; $items=@(Get-BitsTransfer -AllUsers | ForEach-Object { $job=$_; foreach($file in $job.FileList) { [pscustomobject]@{ Job=$job.DisplayName; State=[string]$job.JobState; Source=[string]$file.RemoteName; Destination=[string]$file.LocalName; Transferred=[long]$job.BytesTransferred; Total=[long]$job.BytesTotal } } }); ConvertTo-Json -InputObject $items -Compress -Depth 3";
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add(script);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start PowerShell.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            process.Kill();
            throw new TimeoutException("Reading background transfers timed out.");
        }
        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0) throw new InvalidOperationException(error.Trim());
        return JsonSerializer.Deserialize<List<BitsTransfer>>(output,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
    }
}

public static class BrowserDownloadReader
{
    public static IReadOnlyList<BitsTransfer> Read()
    {
        var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        if (!Directory.Exists(downloads)) return Array.Empty<BitsTransfer>();
        try
        {
            return Directory.EnumerateFiles(downloads)
                .Where(path => path.EndsWith(".crdownload", StringComparison.OrdinalIgnoreCase) ||
                               path.EndsWith(".part", StringComparison.OrdinalIgnoreCase))
                .Select(path => new FileInfo(path))
                .Select(file => new BitsTransfer("Browser partial file", "Downloading", "URL unavailable", file.FullName, file.Length, 0))
                .ToArray();
        }
        catch { return Array.Empty<BitsTransfer>(); }
    }
}
