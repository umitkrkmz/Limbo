using System.Buffers.Binary;
using System.Net;
using System.Text;

namespace Limbo.Core.Dns;

/// <summary>
/// Minimal DNS wire-format support for DoH lookups: build a question, read A/AAAA
/// answers. Wire format works with every RFC 8484 server; the JSON API it replaces
/// is a per-provider extension (Google serves it on another path, Quad9 not at all).
/// </summary>
internal static class DnsMessage
{
    public const ushort TypeA = 1;
    public const ushort TypeAaaa = 28;

    /// <summary>ID 0, as RFC 8484 recommends for cache-friendliness; recursion desired.</summary>
    public static byte[] BuildQuery(string name, ushort type)
    {
        var message = new List<byte> { 0, 0, 0x01, 0x00, 0, 1, 0, 0, 0, 0, 0, 0 };
        foreach (var label in name.TrimEnd('.').Split('.'))
        {
            message.Add((byte)label.Length);
            message.AddRange(Encoding.ASCII.GetBytes(label));
        }
        message.AddRange([0, (byte)(type >> 8), (byte)type, 0, 1]);
        return [.. message];
    }

    /// <summary>
    /// Addresses of the given type in the answer section, plus the smallest TTL among
    /// them. Malformed input yields an empty result rather than an exception.
    /// </summary>
    public static (IPAddress[] Addresses, int MinTtl) ReadAddresses(ReadOnlySpan<byte> response, ushort type)
    {
        var addresses = new List<IPAddress>();
        var minTtl = int.MaxValue;
        if (response.Length < 12 || (response[3] & 0x0F) != 0) return ([], 0);

        var questions = BinaryPrimitives.ReadUInt16BigEndian(response[4..]);
        var answers = BinaryPrimitives.ReadUInt16BigEndian(response[6..]);
        var position = 12;

        for (var i = 0; i < questions; i++)
        {
            if (!SkipName(response, ref position) || position + 4 > response.Length) return ([], 0);
            position += 4;
        }

        for (var i = 0; i < answers; i++)
        {
            if (!SkipName(response, ref position) || position + 10 > response.Length) break;
            var recordType = BinaryPrimitives.ReadUInt16BigEndian(response[position..]);
            var ttl = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(response[(position + 4)..]), int.MaxValue);
            var dataLength = BinaryPrimitives.ReadUInt16BigEndian(response[(position + 8)..]);
            position += 10;
            if (position + dataLength > response.Length) break;

            var expectedLength = type == TypeA ? 4 : 16;
            if (recordType == type && dataLength == expectedLength)
            {
                addresses.Add(new IPAddress(response.Slice(position, dataLength)));
                minTtl = Math.Min(minTtl, ttl);
            }

            position += dataLength;
        }

        return addresses.Count == 0 ? ([], 0) : ([.. addresses], minTtl);
    }

    private static bool SkipName(ReadOnlySpan<byte> message, ref int position)
    {
        while (position < message.Length)
        {
            var length = message[position];
            if (length == 0) { position += 1; return true; }
            if ((length & 0xC0) == 0xC0) { position += 2; return position <= message.Length; } // compression pointer ends the name
            if (length > 63) return false;
            position += 1 + length;
        }

        return false;
    }
}
