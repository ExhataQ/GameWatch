namespace GameWatch.Services;

public sealed class TimeRangeMapper
{
    private const double EdgeSnapPixels = 4;
    private TimeSpan? _followDuration = TimeSpan.FromMinutes(5);
    private bool _pinStart;
    private bool _pinEnd;

    public DateTime Oldest { get; private set; }
    public DateTime Newest { get; private set; }
    public DateTime Start { get; private set; }
    public DateTime End { get; private set; }

    public void SetBounds(DateTime oldest, DateTime newest)
    {
        Oldest = oldest;
        Newest = newest < oldest ? oldest : newest;
        if (_followDuration.HasValue)
        {
            Start = _followDuration.Value == TimeSpan.MaxValue ? Oldest : Max(Oldest, Newest - _followDuration.Value);
            End = Newest;
        }
        else
        {
            Start = _pinStart ? Oldest : Clamp(Start);
            End = _pinEnd ? Newest : Clamp(End);
            if (Start > End) Start = End;
        }
    }

    public void Preset(TimeSpan? duration)
    {
        _followDuration = duration ?? TimeSpan.MaxValue;
        _pinStart = duration is null;
        _pinEnd = true;
        SetBounds(Oldest, Newest);
    }

    public double ToPosition(DateTime value, double width) => Newest <= Oldest ? 0 :
        Math.Clamp((value - Oldest).Ticks / (double)(Newest - Oldest).Ticks, 0, 1) * Math.Max(1, width);

    public DateTime FromPosition(double position, double width) => Oldest.AddTicks(
        (long)(Math.Clamp(position / Math.Max(1, width), 0, 1) * (Newest - Oldest).Ticks));

    public void DragStart(double deltaPixels, double width)
    {
        LeaveFollowMode();
        var position = Math.Clamp(ToPosition(Start, width) + deltaPixels, 0, ToPosition(End, width));
        _pinStart = position <= EdgeSnapPixels;
        Start = _pinStart ? Oldest : FromPosition(position, width);
    }

    public void DragEnd(double deltaPixels, double width)
    {
        LeaveFollowMode();
        var position = Math.Clamp(ToPosition(End, width) + deltaPixels, ToPosition(Start, width), Math.Max(1, width));
        _pinEnd = position >= Math.Max(1, width) - EdgeSnapPixels;
        End = _pinEnd ? Newest : FromPosition(position, width);
    }

    private void LeaveFollowMode()
    {
        if (!_followDuration.HasValue) return;
        _followDuration = null;
        _pinEnd = End == Newest;
        _pinStart = Start == Oldest;
    }

    private DateTime Clamp(DateTime value) => Max(Oldest, value > Newest ? Newest : value);
    private static DateTime Max(DateTime first, DateTime second) => first > second ? first : second;
}
