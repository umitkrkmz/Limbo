using Limbo.Core.Bypass;

namespace Limbo.Tests;

public class BypassStrategyTests
{
    [Fact]
    public void TcpFragmentation_NonTlsPacket_ReturnsOriginal()
    {
        // Arrange
        var strategy = new TcpFragmentationStrategy(2);
        var packet = new byte[100]; // Non-TLS packet
        var context = CreateContext(isClientHello: false);

        // Act
        var results = strategy.Process(packet, context);

        // Assert
        Assert.Single(results);
        Assert.Equal(packet.Length, results[0].Data.Length);
    }

    [Fact]
    public void TcpFragmentation_InboundPacket_ReturnsOriginal()
    {
        // Arrange
        var strategy = new TcpFragmentationStrategy(2);
        var packet = CreateMinimalTlsPacket();
        var context = CreateContext(isOutbound: false, isClientHello: true);

        // Act
        var results = strategy.Process(packet, context);

        // Assert
        Assert.Single(results);
    }

    [Fact]
    public void PacketResult_FromOriginal_PreservesData()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3, 4, 5 };

        // Act
        var result = PacketResult.FromOriginal(data);

        // Assert
        Assert.Equal(data.Length, result.Data.Length);
        Assert.False(result.RecalcChecksum);
    }

    [Fact]
    public void PacketResult_Modified_SetsRecalcFlag()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3, 4, 5 };

        // Act
        var result = PacketResult.Modified(data, recalcChecksum: true);

        // Assert
        Assert.True(result.RecalcChecksum);
    }

    private static PacketContext CreateContext(
        bool isOutbound = true,
        bool isTcp = true,
        bool isClientHello = false)
    {
        return new PacketContext
        {
            IsOutbound = isOutbound,
            IsTcp = isTcp,
            IsUdp = false,
            IsIPv6 = false,
            SrcPort = 8080,
            DstPort = 443,
            IsSyn = false,
            IsClientHello = isClientHello,
            HasHttpHost = false
        };
    }

    private static byte[] CreateMinimalTlsPacket()
    {
        // Minimal IP + TCP + TLS headers
        var packet = new byte[100];
        
        // IP header (20 bytes, IPv4)
        packet[0] = 0x45; // Version 4, IHL 5
        packet[9] = 6;    // Protocol: TCP
        
        // TCP header at offset 20
        // Data offset = 5 (20 bytes), so byte 32 = 0x50
        packet[32] = 0x50;
        
        // TLS record starts at offset 40
        packet[40] = 0x16; // ContentType: Handshake
        packet[45] = 0x01; // HandshakeType: ClientHello
        
        return packet;
    }
}
