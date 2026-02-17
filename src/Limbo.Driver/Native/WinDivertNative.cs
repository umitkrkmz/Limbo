using System.Runtime.InteropServices;

namespace Limbo.Driver.Native;

/// <summary>
/// WinDivert P/Invoke declarations
/// </summary>
internal static partial class WinDivertNative
{
    private const string DllName = "WinDivert.dll";

    [LibraryImport(DllName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint WinDivertOpen(string filter, int layer, short priority, ulong flags);

    [LibraryImport(DllName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WinDivertClose(nint handle);

    [LibraryImport(DllName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WinDivertRecv(
        nint handle,
        Span<byte> pPacket,
        uint packetLen,
        out uint recvLen,
        ref WinDivertAddress pAddr);

    [LibraryImport(DllName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WinDivertSend(
        nint handle,
        ReadOnlySpan<byte> pPacket,
        uint packetLen,
        out uint sendLen,
        ref WinDivertAddress pAddr);

    [LibraryImport(DllName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WinDivertHelperCalcChecksums(
        Span<byte> pPacket,
        uint packetLen,
        ref WinDivertAddress pAddr,
        ulong flags);

    [LibraryImport(DllName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WinDivertSetParam(
        nint handle,
        int param,
        ulong value);

    // Layer constants
    internal const int WINDIVERT_LAYER_NETWORK = 0;
    internal const int WINDIVERT_LAYER_NETWORK_FORWARD = 1;
    
    // Flag constants
    internal const ulong WINDIVERT_FLAG_NONE = 0;
    internal const ulong WINDIVERT_FLAG_SNIFF = 1;
    internal const ulong WINDIVERT_FLAG_DROP = 2;

    // Param constants
    internal const int WINDIVERT_PARAM_QUEUE_LENGTH = 0;
    internal const int WINDIVERT_PARAM_QUEUE_TIME = 1;
    internal const int WINDIVERT_PARAM_QUEUE_SIZE = 2;
}

/// <summary>
/// WinDivert 2.2 Address structure - MUST be 80 bytes!
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 80)]
internal struct WinDivertAddress
{
    [FieldOffset(0)]
    public long Timestamp;          // 8 bytes

    [FieldOffset(8)]
    private uint _layerEventFlags;  // Layer(8) + Event(8) + Flags(16)

    [FieldOffset(12)]
    private uint _reserved2;

    // Network layer data (union at offset 16)
    [FieldOffset(16)]
    public uint IfIdx;

    [FieldOffset(20)]
    public uint SubIfIdx;

    // Layer: bits 0-7
    public byte Layer
    {
        get => (byte)(_layerEventFlags & 0xFF);
        set => _layerEventFlags = (_layerEventFlags & 0xFFFFFF00) | value;
    }

    // Event: bits 8-15
    public byte Event
    {
        get => (byte)((_layerEventFlags >> 8) & 0xFF);
        set => _layerEventFlags = (_layerEventFlags & 0xFFFF00FF) | ((uint)value << 8);
    }

    // Sniffed: bit 16
    public bool Sniffed
    {
        get => (_layerEventFlags & 0x10000) != 0;
        set => _layerEventFlags = value ? (_layerEventFlags | 0x10000) : (_layerEventFlags & ~0x10000u);
    }

    // Outbound: bit 17 - THIS IS THE CRITICAL FIX!
    public bool Outbound
    {
        get => (_layerEventFlags & 0x20000) != 0;
        set => _layerEventFlags = value ? (_layerEventFlags | 0x20000) : (_layerEventFlags & ~0x20000u);
    }

    // Loopback: bit 18
    public bool Loopback
    {
        get => (_layerEventFlags & 0x40000) != 0;
        set => _layerEventFlags = value ? (_layerEventFlags | 0x40000) : (_layerEventFlags & ~0x40000u);
    }

    // Impostor: bit 19
    public bool Impostor
    {
        get => (_layerEventFlags & 0x80000) != 0;
        set => _layerEventFlags = value ? (_layerEventFlags | 0x80000) : (_layerEventFlags & ~0x80000u);
    }

    // IPv6: bit 20
    public bool IPv6
    {
        get => (_layerEventFlags & 0x100000) != 0;
        set => _layerEventFlags = value ? (_layerEventFlags | 0x100000) : (_layerEventFlags & ~0x100000u);
    }
}
