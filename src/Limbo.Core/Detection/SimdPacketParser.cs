using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Limbo.Core.Detection;

/// <summary>
/// SIMD-accelerated packet parsing for high-performance SNI extraction
/// Uses AVX2 when available, falls back to scalar otherwise
/// </summary>
public static class SimdPacketParser
{
    /// <summary>
    /// Check if SIMD acceleration is available
    /// </summary>
    public static bool IsSimdSupported => Avx2.IsSupported;

    /// <summary>
    /// Find SNI extension offset in TLS payload using SIMD when available
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int FindSniOffset(ReadOnlySpan<byte> tlsPayload)
    {
        if (Avx2.IsSupported && tlsPayload.Length >= 64)
            return FindSniOffsetAvx2(tlsPayload);
        
        return FindSniOffsetScalar(tlsPayload);
    }

    /// <summary>
    /// Check if payload contains a specific domain pattern using SIMD
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool ContainsDomain(ReadOnlySpan<byte> payload, ReadOnlySpan<byte> domain)
    {
        if (Avx2.IsSupported && payload.Length >= 32 && domain.Length <= 16)
            return ContainsDomainAvx2(payload, domain);
        
        return payload.IndexOf(domain) >= 0;
    }

    /// <summary>
    /// Extract multiple SNI extensions from a batch of packets (for batch processing)
    /// </summary>
    public static int ExtractSniBatch(
        ReadOnlySpan<byte> packets,
        Span<int> offsets,
        Span<int> lengths,
        int packetCount)
    {
        // For future batch processing optimization
        // Currently just processes sequentially
        var found = 0;
        var currentOffset = 0;

        for (var i = 0; i < packetCount && found < offsets.Length; i++)
        {
            // Assume each packet starts with its length (4 bytes) for batch format
            if (currentOffset + 4 > packets.Length)
                break;

            var packetLen = BitConverter.ToInt32(packets[currentOffset..]);
            currentOffset += 4;

            if (currentOffset + packetLen > packets.Length)
                break;

            var packet = packets.Slice(currentOffset, packetLen);
            var sniOffset = FindSniOffset(packet);

            if (sniOffset >= 0)
            {
                offsets[found] = currentOffset + sniOffset;
                lengths[found] = GetSniLength(packet, sniOffset);
                found++;
            }

            currentOffset += packetLen;
        }

        return found;
    }

    private static unsafe int FindSniOffsetAvx2(ReadOnlySpan<byte> payload)
    {
        // Look for SNI extension pattern: 0x00 0x00 (extension type)
        // followed by length and 0x00 (name type = hostname)
        
        // We search for the pattern [0x00, 0x00, ?, ?, 0x00] which indicates SNI extension start
        // This is faster than parsing the full TLS structure for most cases
        
        if (payload.Length < 32)
            return FindSniOffsetScalar(payload);

        fixed (byte* ptr = payload)
        {
            // Create search pattern: looking for 0x00 bytes that might indicate SNI
            var zeroVec = Vector256<byte>.Zero;
            
            var searchLen = payload.Length - 32;
            
            for (var i = 0; i < searchLen; i += 32)
            {
                var chunk = Avx.LoadVector256(ptr + i);
                var cmpResult = Avx2.CompareEqual(chunk, zeroVec);
                var mask = Avx2.MoveMask(cmpResult);
                
                if (mask != 0)
                {
                    // Found some zeros, check if it's SNI extension
                    for (var j = 0; j < 32 && i + j + 5 < payload.Length; j++)
                    {
                        if ((mask & (1 << j)) != 0)
                        {
                            // Check if this could be SNI extension (type 0x0000)
                            if (j > 0 && ptr[i + j - 1] == 0x00 && ptr[i + j] == 0x00)
                            {
                                // Potential SNI extension start at i + j - 1
                                // Verify by checking structure
                                var extOffset = i + j - 1;
                                if (ValidateSniExtension(payload, extOffset))
                                {
                                    // Return offset to the actual hostname
                                    return extOffset + 9; // Skip ext type(2) + ext len(2) + list len(2) + name type(1) + name len(2)
                                }
                            }
                        }
                    }
                }
            }
        }
        
        // Fall back to scalar for remaining bytes or if pattern not found
        return FindSniOffsetScalar(payload);
    }

    private static int FindSniOffsetScalar(ReadOnlySpan<byte> payload)
    {
        // Standard TLS parsing - same as PacketInspector but focused on offset finding
        if (payload.Length < 43)
            return -1;

        try
        {
            var offset = 9; // Skip TLS record (5) + handshake header (4)
            offset += 34;   // Skip version (2) + random (32)
            
            if (offset >= payload.Length)
                return -1;
            
            offset += 1 + payload[offset]; // Skip session ID
            
            if (offset + 2 >= payload.Length)
                return -1;
            
            offset += 2 + ((payload[offset] << 8) | payload[offset + 1]); // Skip cipher suites
            
            if (offset >= payload.Length)
                return -1;
            
            offset += 1 + payload[offset]; // Skip compression
            
            if (offset + 2 >= payload.Length)
                return -1;
            
            var extLen = (payload[offset] << 8) | payload[offset + 1];
            offset += 2;
            
            var extEnd = offset + extLen;
            
            while (offset + 4 < extEnd && offset + 4 < payload.Length)
            {
                var extType = (payload[offset] << 8) | payload[offset + 1];
                var thisExtLen = (payload[offset + 2] << 8) | payload[offset + 3];
                offset += 4;
                
                if (extType == 0) // SNI extension
                {
                    // Return offset to hostname (after list len(2) + type(1) + name len(2))
                    return offset + 5;
                }
                
                offset += thisExtLen;
            }
        }
        catch
        {
            // Malformed packet
        }
        
        return -1;
    }

    private static bool ValidateSniExtension(ReadOnlySpan<byte> payload, int offset)
    {
        if (offset + 9 >= payload.Length)
            return false;
        
        // Check extension type is 0x0000 (SNI)
        if (payload[offset] != 0x00 || payload[offset + 1] != 0x00)
            return false;
        
        // Check that lengths make sense
        var extLen = (payload[offset + 2] << 8) | payload[offset + 3];
        var listLen = (payload[offset + 4] << 8) | payload[offset + 5];
        
        // List length should be ext length - 2
        if (listLen != extLen - 2)
            return false;
        
        // Name type should be 0x00 (hostname)
        if (payload[offset + 6] != 0x00)
            return false;
        
        return true;
    }

    private static unsafe bool ContainsDomainAvx2(ReadOnlySpan<byte> payload, ReadOnlySpan<byte> domain)
    {
        if (domain.Length == 0 || domain.Length > 16)
            return payload.IndexOf(domain) >= 0;

        fixed (byte* payloadPtr = payload)
        fixed (byte* domainPtr = domain)
        {
            // Broadcast first byte of domain for initial search
            var firstByte = Vector256.Create(domainPtr[0]);
            
            var searchLen = payload.Length - 32;
            
            for (var i = 0; i < searchLen; i += 32)
            {
                var chunk = Avx.LoadVector256(payloadPtr + i);
                var cmpResult = Avx2.CompareEqual(chunk, firstByte);
                var mask = Avx2.MoveMask(cmpResult);
                
                while (mask != 0)
                {
                    var bitPos = BitOperations.TrailingZeroCount(mask);
                    var candidatePos = i + bitPos;
                    
                    if (candidatePos + domain.Length <= payload.Length)
                    {
                        // Verify full match
                        if (payload.Slice(candidatePos, domain.Length).SequenceEqual(domain))
                            return true;
                    }
                    
                    mask &= mask - 1; // Clear lowest set bit
                }
            }
            
            // Check remaining bytes
            if (searchLen > 0)
            {
                return payload[searchLen..].IndexOf(domain) >= 0;
            }
        }
        
        return false;
    }

    private static int GetSniLength(ReadOnlySpan<byte> payload, int sniOffset)
    {
        if (sniOffset < 2 || sniOffset >= payload.Length)
            return 0;
        
        // Name length is 2 bytes before the hostname
        return (payload[sniOffset - 2] << 8) | payload[sniOffset - 1];
    }
}

/// <summary>
/// Bit manipulation utilities using hardware intrinsics
/// </summary>
file static class BitOperations
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int TrailingZeroCount(int value)
    {
        if (System.Numerics.BitOperations.TrailingZeroCount(value) is var result)
            return result;
        return 32;
    }
}
