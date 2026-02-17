namespace Limbo.Core.Monitoring;

/// <summary>
/// Real-time bandwidth monitoring with sliding window calculations
/// </summary>
public class BandwidthMonitor
{
    private readonly object _lock = new();
    private readonly Queue<(DateTime Time, long Bytes, bool IsDownload)> _samples = new();
    private readonly TimeSpan _windowSize;
    
    // Totals
    private long _totalDownload;
    private long _totalUpload;
    
    // Current rates (bytes per second)
    private double _downloadRate;
    private double _uploadRate;
    
    // Peak rates
    private double _peakDownload;
    private double _peakUpload;

    public BandwidthMonitor(TimeSpan? windowSize = null)
    {
        _windowSize = windowSize ?? TimeSpan.FromSeconds(2);
    }

    /// <summary>
    /// Record bytes transferred
    /// </summary>
    public void RecordBytes(long bytes, bool isDownload)
    {
        if (bytes <= 0) return;

        lock (_lock)
        {
            var now = DateTime.UtcNow;
            _samples.Enqueue((now, bytes, isDownload));

            if (isDownload)
                Interlocked.Add(ref _totalDownload, bytes);
            else
                Interlocked.Add(ref _totalUpload, bytes);

            // Remove old samples
            var cutoff = now - _windowSize;
            while (_samples.Count > 0 && _samples.Peek().Time < cutoff)
            {
                _samples.Dequeue();
            }

            // Calculate rates
            CalculateRates();
        }
    }

    private void CalculateRates()
    {
        if (_samples.Count == 0)
        {
            _downloadRate = 0;
            _uploadRate = 0;
            return;
        }

        var now = DateTime.UtcNow;
        var cutoff = now - _windowSize;

        long downloadBytes = 0;
        long uploadBytes = 0;

        foreach (var sample in _samples)
        {
            if (sample.Time >= cutoff)
            {
                if (sample.IsDownload)
                    downloadBytes += sample.Bytes;
                else
                    uploadBytes += sample.Bytes;
            }
        }

        var seconds = _windowSize.TotalSeconds;
        _downloadRate = downloadBytes / seconds;
        _uploadRate = uploadBytes / seconds;

        // Track peaks
        if (_downloadRate > _peakDownload)
            _peakDownload = _downloadRate;
        if (_uploadRate > _peakUpload)
            _peakUpload = _uploadRate;
    }

    /// <summary>
    /// Get current bandwidth statistics
    /// </summary>
    public BandwidthStats GetStats()
    {
        lock (_lock)
        {
            CalculateRates();
            return new BandwidthStats
            {
                DownloadRate = _downloadRate,
                UploadRate = _uploadRate,
                TotalDownload = Interlocked.Read(ref _totalDownload),
                TotalUpload = Interlocked.Read(ref _totalUpload),
                PeakDownload = _peakDownload,
                PeakUpload = _peakUpload
            };
        }
    }

    /// <summary>
    /// Reset all statistics
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _samples.Clear();
            _totalDownload = 0;
            _totalUpload = 0;
            _downloadRate = 0;
            _uploadRate = 0;
            _peakDownload = 0;
            _peakUpload = 0;
        }
    }
}

/// <summary>
/// Bandwidth statistics snapshot
/// </summary>
public class BandwidthStats
{
    /// <summary>
    /// Current download rate (bytes per second)
    /// </summary>
    public double DownloadRate { get; init; }

    /// <summary>
    /// Current upload rate (bytes per second)
    /// </summary>
    public double UploadRate { get; init; }

    /// <summary>
    /// Total bytes downloaded
    /// </summary>
    public long TotalDownload { get; init; }

    /// <summary>
    /// Total bytes uploaded
    /// </summary>
    public long TotalUpload { get; init; }

    /// <summary>
    /// Peak download rate
    /// </summary>
    public double PeakDownload { get; init; }

    /// <summary>
    /// Peak upload rate
    /// </summary>
    public double PeakUpload { get; init; }

    /// <summary>
    /// Format rate as human-readable string
    /// </summary>
    public static string FormatRate(double bytesPerSecond) => bytesPerSecond switch
    {
        >= 1_073_741_824 => $"{bytesPerSecond / 1_073_741_824:F1} GB/s",
        >= 1_048_576 => $"{bytesPerSecond / 1_048_576:F1} MB/s",
        >= 1_024 => $"{bytesPerSecond / 1_024:F1} KB/s",
        _ => $"{bytesPerSecond:F0} B/s"
    };
}
