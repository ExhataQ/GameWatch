using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using GameWatch.Services;

namespace GameWatch;

// One row of the process grid. Unlike Services.ProcessTrafficRow (an
// immutable per-sample snapshot), this is long-lived and mutable: the
// grid's ObservableCollection keeps the same instance per PID across
// refreshes and just updates its properties. That means WPF doesn't tear
// down and rebuild the row's controls (including the Track checkbox)
// every tick, which is what made clicks get lost on refresh.
public class ProcessRowViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public ProcessRowViewModel(int pid)
    {
        Pid = pid;
    }

    public int Pid { get; }

    private string _processName = "";
    public string ProcessName
    {
        get => _processName;
        set { if (_processName == value) return; _processName = value; Raise(); }
    }

    private string _exePath = "";
    public string ExePath
    {
        get => _exePath;
        set { if (_exePath == value) return; _exePath = value; Raise(); Icon = ProcessIconProvider.Get(value); }
    }

    private ImageSource? _icon;
    public ImageSource? Icon
    {
        get => _icon;
        private set { if (_icon == value) return; _icon = value; Raise(); }
    }

    private string _services = "";
    public string Services
    {
        get => _services;
        set { if (_services == value) return; _services = value; Raise(); }
    }

    private string _remoteEndpoints = "";
    public string RemoteEndpoints
    {
        get => _remoteEndpoints;
        set { if (_remoteEndpoints == value) return; _remoteEndpoints = value; Raise(); }
    }

    private int _connectionCount;
    public int ConnectionCount
    {
        get => _connectionCount;
        set { if (_connectionCount == value) return; _connectionCount = value; Raise(); }
    }

    private double _downloadBytesPerSecond;
    public double DownloadBytesPerSecond
    {
        get => _downloadBytesPerSecond;
        set
        {
            if (_downloadBytesPerSecond == value) return;
            _downloadBytesPerSecond = value;
            Raise();
            Raise(nameof(DownloadRateDisplay));
        }
    }

    private double _uploadBytesPerSecond;
    public double UploadBytesPerSecond
    {
        get => _uploadBytesPerSecond;
        set
        {
            if (_uploadBytesPerSecond == value) return;
            _uploadBytesPerSecond = value;
            Raise();
            Raise(nameof(UploadRateDisplay));
        }
    }

    private bool _isTracked;
    public bool IsTracked
    {
        get => _isTracked;
        set { if (_isTracked == value) return; _isTracked = value; Raise(); }
    }

    public string DownloadRateDisplay => RateFormatting.FormatRate(DownloadBytesPerSecond);
    public string UploadRateDisplay => RateFormatting.FormatRate(UploadBytesPerSecond);

    // Setters skip unchanged values, so an unchanged row raises no events
    // and the grid doesn't repaint it.
    public void UpdateFrom(ProcessTrafficRow sample)
    {
        ProcessName = sample.ProcessName;
        ExePath = sample.ExePath;
        Services = sample.Services;
        RemoteEndpoints = sample.RemoteEndpoints;
        ConnectionCount = sample.ConnectionCount;
        DownloadBytesPerSecond = sample.DownloadBytesPerSecond;
        UploadBytesPerSecond = sample.UploadBytesPerSecond;
        IsTracked = sample.IsTracked;
    }
}
