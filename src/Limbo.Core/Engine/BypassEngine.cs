using Limbo.Core.Bypass;
using Limbo.Core.Profiles;

namespace Limbo.Core.Engine;

/// <summary>
/// Manages bypass strategies and selects appropriate one based on profile
/// </summary>
public class BypassEngine
{
    public event Action<string>? OnLog;

    /// <summary>
    /// Process a packet using the specified method with default parameters
    /// </summary>
    public IReadOnlyList<PacketResult> ProcessPacket(
        ReadOnlySpan<byte> packet,
        PacketContext context,
        BypassMethod method)
    {
        if (method == BypassMethod.None)
            return [PacketResult.FromOriginal(packet)];

        var strategy = CreateStrategy(method);
        return strategy.Process(packet, context);
    }

    /// <summary>
    /// Process a packet using profile-specific parameters
    /// </summary>
    public IReadOnlyList<PacketResult> ProcessPacket(
        ReadOnlySpan<byte> packet,
        PacketContext context,
        DomainProfile profile)
    {
        if (profile.Method == BypassMethod.None)
            return [PacketResult.FromOriginal(packet)];

        var strategy = CreateStrategyFromProfile(profile);
        return strategy.Process(packet, context);
    }

    /// <summary>
    /// Create a bypass strategy with default parameters
    /// </summary>
    public static IDpiBypassStrategy CreateStrategy(BypassMethod method)
    {
        return method switch
        {
            BypassMethod.TcpFragmentation => new TcpFragmentationStrategy(),
            BypassMethod.SmartFragmentation => new SmartFragmentationStrategy(),
            BypassMethod.Desync => new DesyncStrategy(),
            BypassMethod.FakeTtl => new FakeTtlStrategy(),
            BypassMethod.UdpFake => new UdpFakeStrategy(),
            BypassMethod.QuicFake => new QuicBypassStrategy(),
            BypassMethod.Ech => new EchBypassStrategy(),
            _ => new TcpFragmentationStrategy()
        };
    }

    /// <summary>
    /// Create a bypass strategy configured with profile-specific parameters
    /// </summary>
    public static IDpiBypassStrategy CreateStrategyFromProfile(DomainProfile profile)
    {
        // If adaptive params, use defaults
        if (profile.UseAdaptiveParams)
            return CreateStrategy(profile.Method);

        return profile.Method switch
        {
            BypassMethod.TcpFragmentation => new TcpFragmentationStrategy(profile.FragmentSize),
            BypassMethod.SmartFragmentation => new SmartFragmentationStrategy(profile.SplitOffset),
            BypassMethod.Desync => new DesyncStrategy(profile.SplitPosition),
            BypassMethod.FakeTtl => new FakeTtlStrategy((byte)Math.Clamp(profile.FakeTtl, 1, 255)),
            BypassMethod.UdpFake => new UdpFakeStrategy(profile.FakeTtl),
            BypassMethod.QuicFake => new QuicBypassStrategy(profile.FakePacketSize),
            BypassMethod.Ech => new EchBypassStrategy(profile.EchPublicName),
            _ => new TcpFragmentationStrategy(profile.FragmentSize)
        };
    }

    /// <summary>
    /// Get all registered strategies ordered by priority (for calibration)
    /// </summary>
    public IReadOnlyList<IDpiBypassStrategy> GetStrategiesInOrder()
    {
        var methods = GetAvailableMethods();
        return methods
            .Select(CreateStrategy)
            .OrderByDescending(s => s.Priority)
            .ToList();
    }

    /// <summary>
    /// Get all available bypass methods (useful for UI)
    /// </summary>
    public static IReadOnlyList<BypassMethod> GetAvailableMethods()
    {
        return
        [
            BypassMethod.TcpFragmentation,
            BypassMethod.SmartFragmentation,
            BypassMethod.Desync,
            BypassMethod.FakeTtl,
            BypassMethod.UdpFake,
            BypassMethod.QuicFake,
            BypassMethod.Ech
        ];
    }

    /// <summary>
    /// Create default engine with all strategies
    /// </summary>
    public static BypassEngine CreateDefault()
    {
        return new BypassEngine();
    }
}
