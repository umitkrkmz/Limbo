using System.Text;

namespace Limbo.Core.Detection;

/// <summary>
/// Packet inspection and TLS analysis utilities
/// </summary>
public static class PacketInspector
{
    /// <summary>
    /// Analyze a packet and extract context information
    /// </summary>
    public static Bypass.PacketContext Analyze(ReadOnlySpan<byte> packet, bool isOutbound)
    {
        if (packet.Length < 20)
        {
            return new Bypass.PacketContext
            {
                IsOutbound = isOutbound,
                IsTcp = false,
                IsUdp = false,
                IsIPv6 = false,
                SrcPort = 0,
                DstPort = 0,
                IsSyn = false,
                IsClientHello = false,
                HasHttpHost = false
            };
        }

        var ipVersion = (packet[0] >> 4) & 0x0F;
        var isIPv6 = ipVersion == 6;
        var ipHeaderLen = isIPv6 ? 40 : (packet[0] & 0x0F) * 4;
        
        if (packet.Length < ipHeaderLen + 8)
        {
            return new Bypass.PacketContext
            {
                IsOutbound = isOutbound,
                IsTcp = false,
                IsUdp = false,
                IsIPv6 = isIPv6,
                SrcPort = 0,
                DstPort = 0,
                IsSyn = false,
                IsClientHello = false,
                HasHttpHost = false
            };
        }

        // Get protocol from IP header
        var protocol = isIPv6 ? packet[6] : packet[9];
        var isTcp = protocol == 6;
        var isUdp = protocol == 17;

        ushort srcPort = 0;
        ushort dstPort = 0;
        bool isSyn = false;
        bool isClientHello = false;
        bool hasHttpHost = false;
        string? targetDomain = null;

        if (isTcp && packet.Length >= ipHeaderLen + 20)
        {
            srcPort = (ushort)((packet[ipHeaderLen] << 8) | packet[ipHeaderLen + 1]);
            dstPort = (ushort)((packet[ipHeaderLen + 2] << 8) | packet[ipHeaderLen + 3]);
            
            var tcpFlags = packet[ipHeaderLen + 13];
            isSyn = (tcpFlags & 0x02) != 0;
            
            var tcpHeaderLen = ((packet[ipHeaderLen + 12] >> 4) & 0x0F) * 4;
            var payloadOffset = ipHeaderLen + tcpHeaderLen;
            
            if (packet.Length > payloadOffset)
            {
                var payload = packet[payloadOffset..];
                isClientHello = IsTlsClientHello(payload);
                
                if (isClientHello)
                {
                    targetDomain = ExtractSni(payload);
                }
                else
                {
                    hasHttpHost = ContainsHttpHost(payload);
                    if (hasHttpHost)
                    {
                        targetDomain = ExtractHttpHost(payload);
                    }
                }
            }
        }
        else if (isUdp && packet.Length >= ipHeaderLen + 8)
        {
            srcPort = (ushort)((packet[ipHeaderLen] << 8) | packet[ipHeaderLen + 1]);
            dstPort = (ushort)((packet[ipHeaderLen + 2] << 8) | packet[ipHeaderLen + 3]);
        }

        return new Bypass.PacketContext
        {
            IsOutbound = isOutbound,
            IsTcp = isTcp,
            IsUdp = isUdp,
            IsIPv6 = isIPv6,
            SrcPort = srcPort,
            DstPort = dstPort,
            IsSyn = isSyn,
            IsClientHello = isClientHello,
            HasHttpHost = hasHttpHost,
            TargetDomain = targetDomain
        };
    }

    /// <summary>
    /// Check if payload is a TLS ClientHello
    /// </summary>
    public static bool IsTlsClientHello(ReadOnlySpan<byte> payload)
    {
        // TLS record: ContentType(1) + Version(2) + Length(2)
        // ContentType 0x16 = Handshake
        // Handshake type 0x01 = ClientHello
        return payload.Length >= 6 && 
               payload[0] == 0x16 && 
               payload[5] == 0x01;
    }

    /// <summary>
    /// Extract SNI from TLS ClientHello
    /// </summary>
    public static string? ExtractSni(ReadOnlySpan<byte> payload)
    {
        if (!TlsClientHello.TryFindServerName(payload, out var hostOffset, out var hostLength)
            || hostOffset + hostLength > payload.Length)
            return null;

        return Encoding.ASCII.GetString(payload.Slice(hostOffset, hostLength));
    }

    /// <summary>
    /// Check if payload contains HTTP Host header
    /// </summary>
    public static bool ContainsHttpHost(ReadOnlySpan<byte> payload)
    {
        // Look for "Host:" in the payload
        var hostBytes = "Host:"u8;
        return payload.IndexOf(hostBytes) >= 0;
    }

    /// <summary>
    /// Extract HTTP Host header value
    /// </summary>
    public static string? ExtractHttpHost(ReadOnlySpan<byte> payload)
    {
        var hostBytes = "Host:"u8;
        var idx = payload.IndexOf(hostBytes);
        if (idx < 0) return null;
        
        idx += 5; // Skip "Host:"
        
        // Skip whitespace
        while (idx < payload.Length && (payload[idx] == ' ' || payload[idx] == '\t'))
            idx++;
        
        // Find end of line
        var endIdx = idx;
        while (endIdx < payload.Length && payload[endIdx] != '\r' && payload[endIdx] != '\n')
            endIdx++;
        
        if (endIdx > idx)
            return Encoding.ASCII.GetString(payload[idx..endIdx]);
        
        return null;
    }
}
