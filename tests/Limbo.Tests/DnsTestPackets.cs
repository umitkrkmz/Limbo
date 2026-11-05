using System.Buffers.Binary;
using System.Net;
using Limbo.Driver;
using Limbo.Driver.Native;

namespace Limbo.Tests;

/// <summary>Builds raw IP+UDP+DNS packets the way the capture loop sees them.</summary>
internal static class DnsTestPackets
{
    public static readonly IPAddress ClientV4 = IPAddress.Parse("192.168.1.10");
    public static readonly IPAddress RouterV4 = IPAddress.Parse("192.168.1.1");
    public static readonly IPAddress ClientV6 = IPAddress.Parse("fe80::1234:5678:9abc:def0");
    // The router DNS from the poisoned-lookup screenshot - the IPv6 path that bypassed
    // the user's encrypted IPv4 DNS setting.
    public static readonly IPAddress RouterV6 = IPAddress.Parse("fe80::fe9f:2aff:fe5b:35d8");

    public const ushort ClientPort = 51234;
    public const ushort QueryId = 0xBEEF;

    public static byte[] DnsQuery(string name, ushort id = QueryId, ushort type = 1)
    {
        var question = new List<byte>();
        foreach (var label in name.Split('.'))
        {
            question.Add((byte)label.Length);
            question.AddRange(System.Text.Encoding.ASCII.GetBytes(label));
        }
        question.Add(0);
        question.AddRange([(byte)(type >> 8), (byte)type, 0, 1]);

        var header = new byte[12];
        BinaryPrimitives.WriteUInt16BigEndian(header, id);
        header[2] = 0x01; // RD
        header[5] = 1;    // QDCOUNT
        return [.. header, .. question];
    }

    public static byte[] Udp(IPAddress src, IPAddress dst, ushort srcPort, ushort dstPort, byte[] payload)
    {
        var isV6 = src.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6;
        var ipLength = isV6 ? 40 : 20;
        var packet = new byte[ipLength + 8 + payload.Length];

        if (isV6)
        {
            packet[0] = 0x60;
            BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(4), (ushort)(8 + payload.Length));
            packet[6] = 17;
            packet[7] = 255;
            src.GetAddressBytes().CopyTo(packet, 8);
            dst.GetAddressBytes().CopyTo(packet, 24);
        }
        else
        {
            packet[0] = 0x45;
            BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2), (ushort)packet.Length);
            BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(4), 0x1A2B);
            packet[8] = 128;
            packet[9] = 17;
            src.GetAddressBytes().CopyTo(packet, 12);
            dst.GetAddressBytes().CopyTo(packet, 16);
        }

        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(ipLength), srcPort);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(ipLength + 2), dstPort);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(ipLength + 4), (ushort)(8 + payload.Length));
        payload.CopyTo(packet, ipLength + 8);
        return packet;
    }

    public static byte[] QueryV4(string name) => Udp(ClientV4, RouterV4, ClientPort, 53, DnsQuery(name));
    public static byte[] QueryV6(string name) => Udp(ClientV6, RouterV6, ClientPort, 53, DnsQuery(name));

    public static PacketAddress Outbound() => new(new WinDivertAddress { Outbound = true, IfIdx = 7 });
    public static PacketAddress Inbound() => new(new WinDivertAddress { Outbound = false, IfIdx = 7 });
}

/// <summary>Records what the interceptor sends instead of touching a real driver.</summary>
internal sealed class RecordingDriver : IPacketDriver
{
    public readonly List<(byte[] Packet, PacketAddress Address, bool AsInboundReply)> Sent = [];
    public readonly TaskCompletionSource FirstSend = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool Send(ReadOnlySpan<byte> packet, PacketAddress address, bool asInboundReply)
    {
        lock (Sent) Sent.Add((packet.ToArray(), address, asInboundReply));
        FirstSend.TrySetResult();
        return true;
    }

    public PacketAddress LastAddress => default;
    public bool IsOpen => true;
    public bool Open(string filter) => true;
    public void Close() { }
    public bool Receive(Span<byte> buffer, out int length, out bool isOutbound) => throw new NotSupportedException();
    public bool Send(ReadOnlySpan<byte> packet, bool recalcChecksum) => throw new NotSupportedException();
    public void Dispose() { }
}
