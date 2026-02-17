using Limbo.Core.Monitoring;

namespace Limbo.Tests;

public class BandwidthMonitorTests
{
    [Fact]
    public void RecordBytes_Download_TracksTotal()
    {
        // Arrange
        var monitor = new BandwidthMonitor();

        // Act
        monitor.RecordBytes(1024, isDownload: true);
        monitor.RecordBytes(2048, isDownload: true);
        var stats = monitor.GetStats();

        // Assert
        Assert.Equal(3072, stats.TotalDownload);
    }

    [Fact]
    public void RecordBytes_Upload_TracksTotal()
    {
        // Arrange
        var monitor = new BandwidthMonitor();

        // Act
        monitor.RecordBytes(512, isDownload: false);
        monitor.RecordBytes(512, isDownload: false);
        var stats = monitor.GetStats();

        // Assert
        Assert.Equal(1024, stats.TotalUpload);
    }

    [Fact]
    public void RecordBytes_Mixed_SeparatesTotals()
    {
        // Arrange
        var monitor = new BandwidthMonitor();

        // Act
        monitor.RecordBytes(1000, isDownload: true);
        monitor.RecordBytes(500, isDownload: false);
        var stats = monitor.GetStats();

        // Assert
        Assert.Equal(1000, stats.TotalDownload);
        Assert.Equal(500, stats.TotalUpload);
    }

    [Fact]
    public void RecordBytes_Zero_Ignored()
    {
        // Arrange
        var monitor = new BandwidthMonitor();

        // Act
        monitor.RecordBytes(0, isDownload: true);
        monitor.RecordBytes(-1, isDownload: false);
        var stats = monitor.GetStats();

        // Assert - zeros and negatives should not be recorded
        Assert.Equal(0, stats.TotalDownload);
        Assert.Equal(0, stats.TotalUpload);
    }

    [Fact]
    public void Reset_ClearsAllStats()
    {
        // Arrange
        var monitor = new BandwidthMonitor();
        monitor.RecordBytes(4096, isDownload: true);
        monitor.RecordBytes(2048, isDownload: false);

        // Act
        monitor.Reset();
        var stats = monitor.GetStats();

        // Assert
        Assert.Equal(0, stats.TotalDownload);
        Assert.Equal(0, stats.TotalUpload);
        Assert.Equal(0, stats.PeakDownload);
        Assert.Equal(0, stats.PeakUpload);
    }

    [Fact]
    public void GetStats_AfterRecording_HasPositiveRate()
    {
        // Arrange - use a short window to ensure samples fall within
        var monitor = new BandwidthMonitor(TimeSpan.FromSeconds(10));

        // Act
        monitor.RecordBytes(1_048_576, isDownload: true); // 1 MB
        var stats = monitor.GetStats();

        // Assert - rate should be positive
        Assert.True(stats.DownloadRate > 0);
    }

    [Fact]
    public void FormatRate_BytesRange_ContainsUnit()
    {
        // FormatRate output is culture-sensitive (decimal separator may vary).
        // We verify the unit suffix is present rather than the exact decimal format.
        Assert.Contains("KB/s", BandwidthStats.FormatRate(1024));
        Assert.Contains("MB/s", BandwidthStats.FormatRate(1_048_576));
        Assert.Contains("GB/s", BandwidthStats.FormatRate(1_073_741_824));
        Assert.Contains("B/s", BandwidthStats.FormatRate(100));
    }

    [Fact]
    public void PeakRate_TracksMaximum()
    {
        // Arrange
        var monitor = new BandwidthMonitor(TimeSpan.FromSeconds(10));

        // Act - record large then small bytes
        monitor.RecordBytes(1_048_576, isDownload: true);
        var peakAfterLarge = monitor.GetStats().PeakDownload;

        monitor.RecordBytes(1, isDownload: true);
        var peakAfterSmall = monitor.GetStats().PeakDownload;

        // Assert - peak should not decrease
        Assert.True(peakAfterSmall >= peakAfterLarge);
    }
}
