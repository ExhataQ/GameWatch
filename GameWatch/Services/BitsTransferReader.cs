using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Media;

namespace GameWatch.Services;

public record BitsTransfer(string Job, string State, string Source, string Destination, long Transferred, long Total, string Service = "")
{
    public string ProgressDisplay => $"{ByteFormatting.FormatBytes(Transferred)} / {(Total > 0 ? ByteFormatting.FormatBytes(Total) : "unknown")}";
    public ImageSource? Icon => ProcessIconProvider.GetFileTypeIcon(Destination);
    public string ServiceHint => Service.Length == 0 ? "" : $"{Service} (svchost)";
}

public static class BitsTransferReader
{
    public static async Task<IReadOnlyList<BitsTransfer>> ReadAsync()
    {
        const string script = "$ErrorActionPreference='Stop'; $items=@(Get-BitsTransfer -AllUsers | ForEach-Object { $job=$_; foreach($file in $job.FileList) { [pscustomobject]@{ Job=$job.DisplayName; State=[string]$job.JobState; Source=[string]$file.RemoteName; Destination=[string]$file.LocalName; Transferred=[long]$job.BytesTransferred; Total=[long]$job.BytesTotal; Service='BITS' } } }); ConvertTo-Json -InputObject $items -Compress -Depth 3";
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add(script);
        return await PowerShellTransferReader.RunAsync(start);
    }
}

public static class DeliveryOptimizationReader
{
    public static async Task<IReadOnlyList<BitsTransfer>> ReadAsync()
    {
        const string script = "$ErrorActionPreference='Stop'; $items=@(Get-DeliveryOptimizationStatus | ForEach-Object { [pscustomobject]@{ Job='Delivery Optimization' + $(if($_.PredefinedCallerApplication){' ('+$_.PredefinedCallerApplication+')'}else{''}); State=[string]$_.Status; Source=[string]$_.SourceURL; Destination=[string]$_.FileId; Transferred=[long]$_.TotalBytesDownloaded; Total=[long]$_.FileSize; Service='DoSvc' } }); ConvertTo-Json -InputObject $items -Compress -Depth 3";
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add(script);
        return await PowerShellTransferReader.RunAsync(start);
    }
}

internal static class PowerShellTransferReader
{
    public static async Task<IReadOnlyList<BitsTransfer>> RunAsync(ProcessStartInfo start)
    {
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start PowerShell.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill();
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
    private static readonly Guid DownloadsFolder = new("374DE290-123F-4565-9164-39C4925E467B");

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath(ref Guid folderId, uint flags, IntPtr token, out IntPtr path);

    public static string? GetDownloadsPath()
    {
        var folder = DownloadsFolder;
        if (SHGetKnownFolderPath(ref folder, 0, IntPtr.Zero, out var pointer) != 0) return null;
        try { return Marshal.PtrToStringUni(pointer); }
        finally { Marshal.FreeCoTaskMem(pointer); }
    }

    public static bool IsPartialFile(string path) => new[] { ".crdownload", ".part", ".partial", ".opdownload" }
        .Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<BitsTransfer> Read(string? downloads = null)
    {
        downloads ??= GetDownloadsPath();
        if (downloads is null || !Directory.Exists(downloads)) return Array.Empty<BitsTransfer>();
        try
        {
            return Directory.EnumerateFiles(downloads)
                .Where(IsPartialFile)
                .Select(path => new FileInfo(path))
                .Select(file => new BitsTransfer("Browser partial file", "Downloading", "URL unavailable", file.FullName, file.Length, 0))
                .ToArray();
        }
        catch { return Array.Empty<BitsTransfer>(); }
    }
}
