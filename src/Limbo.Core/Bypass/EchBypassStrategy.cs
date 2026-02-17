using Limbo.Core.Ech;

namespace Limbo.Core.Bypass;

/// <summary>
/// ECH (Encrypted Client Hello) bypass strategy
/// Encrypts SNI so DPI cannot see target domain
/// </summary>
public class EchBypassStrategy : IDpiBypassStrategy
{
    public string Name => "ECH (Encrypted Client Hello)";
    public int Priority => 200; // Higher priority than fragmentation
    public bool SupportsTcp => true;
    public bool SupportsUdp => false;

    private readonly EchConfigFetcher _configFetcher = new();
    private readonly string? _customPublicName;

    public EchBypassStrategy(string? customPublicName = null)
    {
        _customPublicName = customPublicName;
    }

    public IReadOnlyList<PacketResult> Process(ReadOnlySpan<byte> packet, PacketContext context)
    {
        // Only process TLS ClientHello with SNI
        if (!context.IsClientHello || string.IsNullOrEmpty(context.TargetDomain))
        {
            return [PacketResult.FromOriginal(packet)];
        }

        // Try to get ECH config for target domain
        var echConfig = GetEchConfig(context.TargetDomain);
        if (echConfig == null)
        {
            // No ECH config available, fall back to forwarding
            return [PacketResult.FromOriginal(packet)];
        }

        // Apply custom public name if set
        if (!string.IsNullOrEmpty(_customPublicName))
        {
            echConfig = echConfig with { PublicName = _customPublicName };
        }

        // Wrap ClientHello with ECH
        var wrappedPacket = EchClientHelloBuilder.WrapClientHello(
            packet, 
            echConfig, 
            context.TargetDomain);

        if (wrappedPacket == null)
        {
            // Failed to wrap, forward original
            return [PacketResult.FromOriginal(packet)];
        }

        return [PacketResult.Modified(wrappedPacket, recalcChecksum: true)];
    }

    private EchConfig? GetEchConfig(string domain)
    {
        // Try well-known configs first (faster)
        var wellKnown = EchConfigFetcher.GetWellKnownConfig(domain);
        if (wellKnown != null) return wellKnown;

        // Try to fetch from DNS (async, but we block here for simplicity)
        try
        {
            return _configFetcher.FetchAsync(domain).GetAwaiter().GetResult();
        }
        catch
        {
            return null;
        }
    }
}
