using System.Windows.Media;
using GameWatch.Services;

namespace GameWatch;

public class TransferDisplayRow
{
    private readonly TransferTotal _total;
    public TransferDisplayRow(TransferTotal total) => _total = total;
    public string ProcessName => _total.ProcessName;
    public string ExePath => _total.ExePath;
    public long DownloadBytes => _total.DownloadBytes;
    public long UploadBytes => _total.UploadBytes;
    public string DownloadDisplay => _total.DownloadDisplay;
    public string UploadDisplay => _total.UploadDisplay;
    public ImageSource? Icon => ProcessIconProvider.Get(ExePath);
}
