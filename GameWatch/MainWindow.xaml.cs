using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using GameWatch.Services;

namespace GameWatch;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings;
    private readonly GameModeController _gameMode;
    private readonly NetworkMonitorEngine _engine;
    private readonly DispatcherTimer _uiTimer;

    // Bound to the grid ONCE. Refreshes update these row objects in place
    // (see MergeRows) instead of replacing the grid's ItemsSource, so
    // checkboxes and header sorting aren't torn down every tick.
    private readonly ObservableCollection<ProcessRowViewModel> _rows = new();
    private readonly Dictionary<int, ProcessRowViewModel> _rowsByPid = new();

    private bool _initializing = true;
    private bool _gameModeBusy;
    private bool _followRecent = true;
    private DateTime _rangeStart;
    private DateTime _rangeEnd;
    private DateTime _oldest;
    private DateTime _newest;
    private IReadOnlyList<TrafficInterval> _latestTransfers = Array.Empty<TrafficInterval>();

    public MainWindow()
    {
        InitializeComponent();

        _settings = AppSettings.Load();

        // If GameWatch crashed or was killed last time while Game Mode
        // was on, undo whatever it left changed before doing anything else.
        GameModeController.TryRecoverFromCrash();

        _gameMode = new GameModeController
        {
            ServiceNames = _settings.GameModeServiceNames,
            ProcessNames = _settings.GameModeProcessNames
        };

        _engine = new NetworkMonitorEngine(_gameMode.IsProcessTracked)
        {
            AdapterFilter = _settings.AdapterFilter
        };

        SetUpGrid();

        RefreshSpeedCombo.SelectedIndex = (int)_settings.UiRefreshSpeed;
        AdapterFilterCombo.SelectedIndex = (int)_settings.AdapterFilter;
        _initializing = false;

        bool etwStarted = _engine.Start();
        EtwStatusText.Text = etwStarted
            ? "Per-app rates: tracking (ETW)"
            : "Per-app rates unavailable - try running as Administrator";
        EtwStatusText.Foreground = etwStarted ? Brushes.MediumSeaGreen : Brushes.OrangeRed;

        // Purely a repaint cadence - only reads _engine.LatestSnapshot.
        _uiTimer = new DispatcherTimer { Interval = AppSettings.IntervalFor(_settings.UiRefreshSpeed) };
        _uiTimer.Tick += UiTick;

        Closing += (_, _) =>
        {
            _uiTimer.Stop();
            _engine.Dispose();
            if (_gameMode.IsOn) _gameMode.Disable();

            _settings.GameModeServiceNames = _gameMode.ServiceNames;
            _settings.GameModeProcessNames = _gameMode.GetProcessNamesSnapshot();
            _settings.Save();
        };

        _uiTimer.Start();
    }

    private void SetUpGrid()
    {
        var view = CollectionViewSource.GetDefaultView(_rows);

        // Live sorting: rows re-order themselves as rates change, using
        // whatever sort the user picked by clicking a column header.
        if (view is ICollectionViewLiveShaping live && live.CanChangeLiveSorting)
        {
            live.LiveSortingProperties.Add(nameof(ProcessRowViewModel.ConnectionCount));
            live.LiveSortingProperties.Add(nameof(ProcessRowViewModel.DownloadBytesPerSecond));
            live.LiveSortingProperties.Add(nameof(ProcessRowViewModel.UploadBytesPerSecond));
            live.LiveSortingProperties.Add(nameof(ProcessRowViewModel.ProcessName));
            live.LiveSortingProperties.Add(nameof(ProcessRowViewModel.Pid));
            live.IsLiveSorting = true;
        }

        // Default order until the user clicks a header: busiest first.
        view.SortDescriptions.Add(new SortDescription(nameof(ProcessRowViewModel.ConnectionCount), ListSortDirection.Descending));

        ConnectionsGrid.ItemsSource = _rows;
    }

    private void UiTick(object? sender, EventArgs e)
    {
        var snap = _engine.LatestSnapshot;
        if (snap is null) return;

        DownloadText.Text = $"↓ {RateFormatting.FormatRate(snap.TotalDownloadBps)}";
        UploadText.Text = $"↑ {RateFormatting.FormatRate(snap.TotalUploadBps)}";
        DownloadText.Foreground = BrushForLevel(RateFormatting.GetRateLevel(snap.TotalDownloadBps));
        UploadText.Foreground = BrushForLevel(RateFormatting.GetRateLevel(snap.TotalUploadBps));
        TimestampText.Text = $"Last updated: {snap.Timestamp:HH:mm:ss}";

        EtwStatusText.Text = snap.EtwRunning
            ? "Per-app rates: tracking (ETW)"
            : $"Per-app rates unavailable: {snap.EtwError ?? "unknown error"} - try running as Administrator";
        EtwStatusText.Foreground = snap.EtwRunning ? Brushes.MediumSeaGreen : Brushes.OrangeRed;

        MergeRows(snap.Processes);
        ConnectionCountText.Text = $"{snap.Processes.Sum(p => p.ConnectionCount)} active connections";

        DrawHistoryGraph(snap.History);
        UpdateTransferRange(snap);
        CheckBandwidthAlert(snap);
    }

    // Update existing rows in place, add new PIDs, drop vanished ones.
    private void MergeRows(IReadOnlyList<ProcessTrafficRow> samples)
    {
        var seen = new HashSet<int>();

        foreach (var sample in samples)
        {
            seen.Add(sample.Pid);
            if (!_rowsByPid.TryGetValue(sample.Pid, out var row))
            {
                row = new ProcessRowViewModel(sample.Pid);
                row.UpdateFrom(sample);
                _rowsByPid[sample.Pid] = row;
                _rows.Add(row);
            }
            else
            {
                row.UpdateFrom(sample);
            }
        }

        for (int i = _rows.Count - 1; i >= 0; i--)
        {
            if (!seen.Contains(_rows[i].Pid))
            {
                _rowsByPid.Remove(_rows[i].Pid);
                _rows.RemoveAt(i);
            }
        }
    }

    private void DrawHistoryGraph(IReadOnlyList<TrafficHistoryPoint> history)
    {
        TrafficGraphCanvas.Children.Clear();
        if (history.Count < 2) return;

        double w = TrafficGraphCanvas.ActualWidth > 0 ? TrafficGraphCanvas.ActualWidth : 400;
        double h = TrafficGraphCanvas.ActualHeight > 0 ? TrafficGraphCanvas.ActualHeight : 80;

        double max = 1;
        foreach (var p in history) max = Math.Max(max, Math.Max(p.DownloadBytesPerSecond, p.UploadBytesPerSecond));

        var downPoints = new PointCollection();
        var upPoints = new PointCollection();

        for (int i = 0; i < history.Count; i++)
        {
            double x = w * i / (history.Count - 1);
            downPoints.Add(new Point(x, h - history[i].DownloadBytesPerSecond / max * h));
            upPoints.Add(new Point(x, h - history[i].UploadBytesPerSecond / max * h));
        }

        TrafficGraphCanvas.Children.Add(new Polyline { Points = downPoints, Stroke = Brushes.MediumSeaGreen, StrokeThickness = 1.5 });
        TrafficGraphCanvas.Children.Add(new Polyline { Points = upPoints, Stroke = Brushes.Gold, StrokeThickness = 1.5 });
    }

    private void UpdateTransferRange(MonitorSnapshot snapshot)
    {
        if (ReferenceEquals(_latestTransfers, snapshot.Transfers)) return;
        _latestTransfers = snapshot.Transfers;
        if (_latestTransfers.Count == 0)
        {
            RangeTotalsText.Text = snapshot.EtwRunning ? "Collecting traffic..." : "ETW monitoring is unavailable.";
            return;
        }

        _oldest = _latestTransfers[0].Start;
        _newest = _latestTransfers[^1].End;
        if (_followRecent)
        {
            _rangeStart = _newest - TimeSpan.FromMinutes(5);
            if (_rangeStart < _oldest) _rangeStart = _oldest;
            _rangeEnd = _newest;
        }
        else
        {
            if (_rangeStart < _oldest) _rangeStart = _oldest;
            if (_rangeEnd < _oldest) _rangeEnd = _oldest;
        }
        RenderRange();
    }

    private double RangeWidth => Math.Max(1, RangeCanvas.ActualWidth - StartThumb.Width);

    private double ToPosition(DateTime value) => _newest <= _oldest ? 0 :
        Math.Clamp((value - _oldest).Ticks / (double)(_newest - _oldest).Ticks, 0, 1) * RangeWidth;

    private DateTime FromPosition(double position) => _oldest.AddTicks(
        (long)(Math.Clamp(position / RangeWidth, 0, 1) * (_newest - _oldest).Ticks));

    private void RenderRange()
    {
        if (RangeCanvas is null || _latestTransfers.Count == 0) return;
        var left = ToPosition(_rangeStart);
        var right = ToPosition(_rangeEnd);
        RangeTrack.Width = RangeWidth + StartThumb.Width;
        Canvas.SetLeft(StartThumb, left);
        Canvas.SetLeft(EndThumb, right);
        Canvas.SetLeft(RangeSelection, left + StartThumb.Width / 2);
        RangeSelection.Width = Math.Max(0, right - left);
        RangeText.Text = $"{_rangeStart:yyyy-MM-dd HH:mm:ss}  →  {_rangeEnd:yyyy-MM-dd HH:mm:ss}  (available: {_oldest:yyyy-MM-dd HH:mm:ss} – {_newest:yyyy-MM-dd HH:mm:ss})";
        var rows = ProcessTrafficHistory.Summarize(_latestTransfers, _rangeStart, _rangeEnd);
        HistoryGrid.ItemsSource = rows.Select(row => new TransferDisplayRow(row)).ToArray();
        RangeTotalsText.Text = $"Downloaded {ByteFormatting.FormatBytes(rows.Sum(row => row.DownloadBytes))}   ·   Uploaded {ByteFormatting.FormatBytes(rows.Sum(row => row.UploadBytes))}   ·   {rows.Count} applications";
    }

    private void RangeCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => RenderRange();

    private void StartThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (_latestTransfers.Count == 0) return;
        _followRecent = false;
        _rangeStart = FromPosition(Math.Min(ToPosition(_rangeEnd), ToPosition(_rangeStart) + e.HorizontalChange));
        RenderRange();
    }

    private void EndThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (_latestTransfers.Count == 0) return;
        _followRecent = false;
        _rangeEnd = FromPosition(Math.Max(ToPosition(_rangeStart), ToPosition(_rangeEnd) + e.HorizontalChange));
        RenderRange();
    }

    private void RecentRange_Click(object sender, RoutedEventArgs e)
    {
        _followRecent = true;
        if (_latestTransfers.Count > 0)
        {
            _rangeStart = _newest - TimeSpan.FromMinutes(5);
            if (_rangeStart < _oldest) _rangeStart = _oldest;
            _rangeEnd = _newest;
            RenderRange();
        }
    }

    private async void RefreshBits_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        button.IsEnabled = false;
        BitsStatusText.Text = "Reading BITS jobs...";
        try
        {
            var jobs = (await BitsTransferReader.ReadAsync()).Concat(BrowserDownloadReader.Read()).ToArray();
            BitsGrid.ItemsSource = jobs;
            BitsStatusText.Text = jobs.Length == 0 ? "No active BITS jobs or browser partial files in Downloads." : $"{jobs.Length} transfer files found.";
        }
        catch (Exception error)
        {
            var browserFiles = BrowserDownloadReader.Read();
            BitsGrid.ItemsSource = browserFiles;
            BitsStatusText.Text = $"BITS unavailable ({error.Message}); {browserFiles.Count} browser partial files found.";
        }
        finally { button.IsEnabled = true; }
    }

    private void CheckBandwidthAlert(MonitorSnapshot snap)
    {
        if (_settings.BandwidthAlertThresholdMBps <= 0)
        {
            AlertText.Text = "";
            return;
        }

        var thresholdBps = _settings.BandwidthAlertThresholdMBps * 1024 * 1024;
        var offender = snap.Processes
            .Where(p => !p.IsTracked && p.DownloadBytesPerSecond + p.UploadBytesPerSecond >= thresholdBps)
            .OrderByDescending(p => p.DownloadBytesPerSecond + p.UploadBytesPerSecond)
            .FirstOrDefault();

        AlertText.Text = offender is null
            ? ""
            : $"⚠ {offender.ProcessName} is using {RateFormatting.FormatRate(offender.DownloadBytesPerSecond + offender.UploadBytesPerSecond)}";
    }

    private void TrackCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ProcessRowViewModel row)
            _gameMode.AddProcessName(row.ProcessName);
    }

    private void TrackCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ProcessRowViewModel row)
            _gameMode.RemoveProcessName(row.ProcessName);
    }

    // async void is fine for a top-level UI event handler. Enable()/Disable()
    // block on service stop/start (up to several seconds each), so they run
    // on a worker thread to keep the window responsive.
    private async void GameModeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_gameModeBusy) return;
        _gameModeBusy = true;
        GameModeButton.IsEnabled = false;

        try
        {
            if (_gameMode.IsOn)
            {
                GameModeButton.Content = "Disabling...";
                await Task.Run(() => _gameMode.Disable());
                GameModeStatusText.Text = "OFF";
                GameModeStatusText.Foreground = Brushes.Gray;
                GameModeButton.Content = "Enable Game Mode";
            }
            else
            {
                GameModeButton.Content = "Enabling...";
                await Task.Run(() => _gameMode.Enable());
                GameModeStatusText.Text = "ON";
                GameModeStatusText.Foreground = Brushes.LightGreen;
                GameModeButton.Content = "Disable Game Mode";
            }
        }
        catch (Exception ex)
        {
            GameModeButton.Content = _gameMode.IsOn ? "Disable Game Mode" : "Enable Game Mode";
            MessageBox.Show($"Game Mode failed: {ex.Message}", "GameWatch");
        }
        finally
        {
            _gameModeBusy = false;
            GameModeButton.IsEnabled = true;
        }
    }

    private void RefreshSpeedCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        var speed = (RefreshSpeed)RefreshSpeedCombo.SelectedIndex;
        _settings.UiRefreshSpeed = speed;
        _uiTimer.Interval = AppSettings.IntervalFor(speed);
        _settings.Save();
    }

    private void AdapterFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        var filter = (AdapterFilterMode)AdapterFilterCombo.SelectedIndex;
        _settings.AdapterFilter = filter;
        _engine.AdapterFilter = filter;
        _settings.Save();
    }

    private static Brush BrushForLevel(RateLevel level) => level switch
    {
        RateLevel.High => Brushes.OrangeRed,
        RateLevel.Elevated => Brushes.Gold,
        _ => Brushes.LightGreen
    };
}
