using System.Buffers.Binary;

namespace Limbo.Core.Detection;

/// <summary>
/// Structured lookups in a TLS ClientHello record (record header, handshake header,
/// version, random, session ID, cipher suites, compression, extensions). Walking the
/// structure skips whole blocks - including a ~1.2 KB post-quantum key_share - in a
/// handful of steps, which measured 2-6x faster than a SIMD byte scan for the SNI and
/// can't lock onto a lookalike byte pattern. Every read is bounds-checked, so a
/// truncated or malformed record yields "not found" instead of an exception.
/// </summary>
internal static class TlsClientHello
{
    private const int ExtensionTypeServerName = 0x0000;

    /// <summary>Offset of the 2-byte extensions-length field, or -1.</summary>
    public static int FindExtensionsLengthOffset(ReadOnlySpan<byte> record)
    {
        var offset = 5 + 4 + 2 + 32; // record header + handshake header + version + random
        if (offset >= record.Length) return -1;

        offset += 1 + record[offset]; // session ID
        if (offset + 2 > record.Length) return -1;

        offset += 2 + BinaryPrimitives.ReadUInt16BigEndian(record[offset..]); // cipher suites
        if (offset >= record.Length) return -1;

        offset += 1 + record[offset]; // compression methods
        return offset + 2 <= record.Length ? offset : -1;
    }

    /// <summary>
    /// Start of the extension of the given type (at its type field) and its total length
    /// including the 4-byte header, or (-1, 0). Only the header has to be inside the record.
    /// </summary>
    public static (int Offset, int Length) FindExtension(ReadOnlySpan<byte> record, int type)
    {
        var lengthOffset = FindExtensionsLengthOffset(record);
        if (lengthOffset < 0) return (-1, 0);

        var offset = lengthOffset + 2;
        var end = offset + BinaryPrimitives.ReadUInt16BigEndian(record[lengthOffset..]);

        while (offset + 4 <= end && offset + 4 <= record.Length)
        {
            var extensionType = BinaryPrimitives.ReadUInt16BigEndian(record[offset..]);
            var dataLength = BinaryPrimitives.ReadUInt16BigEndian(record[(offset + 2)..]);
            if (extensionType == type)
                return (offset, 4 + dataLength);

            offset += 4 + dataLength;
        }

        return (-1, 0);
    }

    /// <summary>
    /// Offset and length of the host name in the server_name (SNI) extension. True when
    /// the name starts inside <paramref name="record"/>; it may extend past the end if the
    /// ClientHello continues in the next segment - check the length before reading it whole.
    /// </summary>
    public static bool TryFindServerName(ReadOnlySpan<byte> record, out int hostOffset, out int hostLength)
    {
        hostOffset = -1;
        hostLength = 0;

        var (extension, _) = FindExtension(record, ExtensionTypeServerName);
        if (extension < 0) return false;

        // extension header (4) + server_name_list length (2) + name type (1) + name length (2)
        var nameLengthOffset = extension + 4 + 2 + 1;
        if (nameLengthOffset + 2 >= record.Length) return false;

        hostOffset = nameLengthOffset + 2;
        hostLength = BinaryPrimitives.ReadUInt16BigEndian(record[nameLengthOffset..]);
        return true;
    }
}
