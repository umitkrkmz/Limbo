using Limbo.Core.Detection;

namespace Limbo.Tests;

public class PacketInspectorTests
{
    [Fact]
    public void IsTlsClientHello_ValidClientHello_ReturnsTrue()
    {
        // Arrange - Minimal TLS ClientHello
        var payload = new byte[]
        {
            0x16, 0x03, 0x01, 0x00, 0x50, // TLS record header
            0x01, 0x00, 0x00, 0x4C,       // Handshake header
            // ... rest of ClientHello would follow
        };

        // Act
        var result = PacketInspector.IsTlsClientHello(payload);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsTlsClientHello_NonTls_ReturnsFalse()
    {
        // Arrange
        var payload = new byte[] { 0x47, 0x45, 0x54, 0x20 }; // "GET "

        // Act
        var result = PacketInspector.IsTlsClientHello(payload);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ContainsHttpHost_WithHost_ReturnsTrue()
    {
        // Arrange
        var payload = "GET / HTTP/1.1\r\nHost: example.com\r\n\r\n"u8.ToArray();

        // Act
        var result = PacketInspector.ContainsHttpHost(payload);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ExtractHttpHost_ValidHeader_ReturnsHost()
    {
        // Arrange
        var payload = "GET / HTTP/1.1\r\nHost: example.com\r\n\r\n"u8.ToArray();

        // Act
        var result = PacketInspector.ExtractHttpHost(payload);

        // Assert
        Assert.Equal("example.com", result);
    }

    [Fact]
    public void Analyze_TcpPacket_SetsIsTcp()
    {
        // Arrange
        var packet = CreateTcpPacket();

        // Act
        var context = PacketInspector.Analyze(packet, isOutbound: true);

        // Assert
        Assert.True(context.IsTcp);
        Assert.True(context.IsOutbound);
    }

    [Fact]
    public void Analyze_ShortPacket_ReturnsEmptyContext()
    {
        // Arrange
        var packet = new byte[10]; // Too short

        // Act
        var context = PacketInspector.Analyze(packet, isOutbound: true);

        // Assert
        Assert.False(context.IsTcp);
        Assert.False(context.IsUdp);
    }

    private static byte[] CreateTcpPacket()
    {
        var packet = new byte[60];
        
        // IPv4 header
        packet[0] = 0x45; // Version 4, IHL 5 (20 bytes)
        packet[9] = 6;    // Protocol: TCP
        
        // Source port (offset 20)
        packet[20] = 0x1F; // Port 8080
        packet[21] = 0x90;
        
        // Dest port (offset 22)
        packet[22] = 0x01; // Port 443
        packet[23] = 0xBB;
        
        // TCP header length (offset 32): 5 * 4 = 20 bytes
        packet[32] = 0x50;
        
        return packet;
    }
}
