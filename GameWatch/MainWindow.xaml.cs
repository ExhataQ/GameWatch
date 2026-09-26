using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using GameWatch.Services;

namespace GameWatch;

public partial class MainWindow : Window
{
    private readonly NetworkStatsService _netService = new();
    private readonly ConnectionsService _connectionsService = new();
    private readonly GameModeController _gameMode = new();
    private readonly EtwNetworkMonitor _etwMonitor = new();
    private readonly DispatcherTimer _timer;
    private NetworkSample? _previous;

    private Dictionary<int, (long Sent, long Received)> _prevEtwSnapshot = new();
    private DateTime _prevEtwSampleTime = DateTime.Now;

    public MainWindow()
    {
        InitializeComponent();

        // Take a baseline sample immediately so the very first tick
        // already has something to diff against.
        _previous = _netService.GetSample();

        bool etwStarted = _etwMonitor.Start();
        EtwStatusText.Text = etwStarted
            ? "Per-app rates: tracking (ETW)"
            : $"Per-app rates unavailable: {_etwMonitor.LastError ?? "unknown error"} - try running as Administrator";
        EtwStatusText.Foreground = etwStarted ? Brushes.MediumSeaGreen : Brushes.OrangeRed;
        _prevEtwSampleTime = DateTime.Now;

        // Make sure the kernel trace session and any suspended
        // processes/services never get left in a bad state.
        this.Closing += (_, _) =>
        {
            _timer.Stop();
            _etwMonitor.Dispose();
            if (_gameMode.IsOn) _gameMode.Disable();
        };

        // DispatcherTimer runs on the UI thread automatically - unlike a
        // background loop, we don't need to worry about cross-thread UI
        // updates here.
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += Timer_Tick;
        _timer.Start();
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        var current = _netService.GetSample();
        if (current is null) return;

        if (_previous is not null)
        {
            var seconds = (current.Timestamp - _previous.Timestamp).TotalSeconds;
            if (seconds <= 0) seconds = 2;

            var download = Math.Max(0, (current.Received - _previous.Received) / seconds);
            var upload = Math.Max(0, (current.Sent - _previous.Sent) / seconds);

            DownloadText.Text = $"Download : {RateFormatting.FormatRate(download)}";
            UploadText.Text = $"Upload   : {RateFormatting.FormatRate(upload)}";
            DownloadText.Foreground = BrushForLevel(RateFormatting.GetRateLevel(download));
            UploadText.Foreground = BrushForLevel(RateFormatting.GetRateLevel(upload));
        }

        TimestampText.Text = $"Last updated: {current.Timestamp:HH:mm:ss}";
        _previous = current;

        // Rebuild the grid's rows from a fresh netstat snapshot each tick.
        // Each row's checkbox reflects whether that process name is
        // currently in the Game Mode target list, so state survives the
        // rebuild even though these are brand-new objects every time.
        var rows = _connectionsService.GetTopProcessesByConnectionCount(_gameMode.ProcessNames, top: 30);

        if (_etwMonitor.IsRunning)
        {
            var etwNow = _etwMonitor.Snapshot();
            var etwNowTime = DateTime.Now;
            var elapsed = (etwNowTime - _prevEtwSampleTime).TotalSeconds;
            if (elapsed <= 0) elapsed = 2;

            foreach (var row in rows)
            {
                if (etwNow.TryGetValue(row.Pid, out var cur))
                {
                    if (_prevEtwSnapshot.TryGetValue(row.Pid, out var prev))
                    {
                        var downBps = Math.Max(0, (cur.Received - prev.Received) / elapsed);
                        var upBps = Math.Max(0, (cur.Sent - prev.Sent) / elapsed);
                        row.DownloadRate = RateFormatting.FormatRate(downBps);
                        row.UploadRate = RateFormatting.FormatRate(upBps);
                    }
                    else
                    {
                        row.DownloadRate = "…";
                        row.UploadRate = "…";
                    }
                }
            }

            _prevEtwSnapshot = etwNow;
            _prevEtwSampleTime = etwNowTime;
        }

        // Only sort by connections here since we don't have a top-N cutoff
        // tied to bandwidth yet - keeps the busiest talkers visible.
        ConnectionsGrid.ItemsSource = rows;
    }

    private void TrackCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ProcessConnectionInfo info)
        {
            if (!_gameMode.ProcessNames.Contains(info.ProcessName, StringComparer.OrdinalIgnoreCase))
                _gameMode.ProcessNames.Add(info.ProcessName);
        }
    }

    private void TrackCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ProcessConnectionInfo info)
        {
            _gameMode.ProcessNames.RemoveAll(n => string.Equals(n, info.ProcessName, StringComparison.OrdinalIgnoreCase));
        }
    }

    private void GameModeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_gameMode.IsOn)
        {
            _gameMode.Disable();
            GameModeStatusText.Text = "OFF";
            GameModeStatusText.Foreground = Brushes.Gray;
            GameModeButton.Content = "Enable Game Mode";
        }
        else
        {
            _gameMode.Enable();
            GameModeStatusText.Text = "ON";
            GameModeStatusText.Foreground = Brushes.LightGreen;
            GameModeButton.Content = "Disable Game Mode";
        }
    }

    private static Brush BrushForLevel(RateLevel level) => level switch
    {
        RateLevel.High => Brushes.OrangeRed,
        RateLevel.Elevated => Brushes.Gold,
        _ => Brushes.LightGreen
    };
}
