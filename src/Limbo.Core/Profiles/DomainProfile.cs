using Limbo.Core.Bypass;

namespace Limbo.Core.Profiles;

/// <summary>
/// Per-domain bypass configuration. Supports exact domains ("gateway.discord.com")
/// and leading-wildcard patterns ("*.discord.com").
/// </summary>
public class DomainProfile
{
    public required string Domain { get; set; }

    public BypassMethod Method { get; set; } = BypassMethod.None;

    /// <summary>Fragment size for TCP/Smart fragmentation (bytes)</summary>
    public int FragmentSize { get; set; } = 2;

    /// <summary>Split position for Desync strategy (bytes from start)</summary>
    public int SplitPosition { get; set; } = 3;

    /// <summary>Split offset from SNI for SmartFragmentation strategy</summary>
    public int SplitOffset { get; set; } = 3;

    /// <summary>TTL value for FakeTtl and UdpFake strategies</summary>
    public int FakeTtl { get; set; } = 3;

    /// <summary>Custom public name for ECH strategy (empty = auto-detect)</summary>
    public string EchPublicName { get; set; } = "";

    /// <summary>Fake packet size for QuicFake strategy (bytes)</summary>
    public int FakePacketSize { get; set; } = 64;

    /// <summary>Whether to use adaptive (auto-tuned) parameters instead of the fields above</summary>
    public bool UseAdaptiveParams { get; set; } = true;

    /// <summary>Whether calibration has confirmed this profile actually works</summary>
    public bool IsVerified { get; set; } = false;

    /// <summary>Last time CalibrationEngine verified this profile, if ever</summary>
    public DateTime? LastCalibrated { get; set; }

    /// <summary>Observed success rate (0-100) from calibration</summary>
    public double SuccessRate { get; set; } = 0;
}
