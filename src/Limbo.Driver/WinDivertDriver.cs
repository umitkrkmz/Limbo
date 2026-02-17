using Limbo.Driver.Native;

namespace Limbo.Driver;

/// <summary>
/// WinDivert packet capture driver implementation
/// </summary>
public class WinDivertDriver : IPacketDriver
{
    private nint _handle;
    private WinDivertAddress _lastAddress;
    private bool _disposed;

    public bool IsOpen => _handle != nint.Zero && _handle != -1;

    public bool Open(string filter)
    {
        if (IsOpen)
            throw new InvalidOperationException("Driver is already open");

        _handle = WinDivertNative.WinDivertOpen(
            filter,
            WinDivertNative.WINDIVERT_LAYER_NETWORK,
            0,
            WinDivertNative.WINDIVERT_FLAG_NONE);

        if (!IsOpen)
            return false;

        // Set queue parameters for better performance (like ZeroDivert)
        WinDivertNative.WinDivertSetParam(_handle, WinDivertNative.WINDIVERT_PARAM_QUEUE_LENGTH, 16384);
        WinDivertNative.WinDivertSetParam(_handle, WinDivertNative.WINDIVERT_PARAM_QUEUE_TIME, 8000);
        WinDivertNative.WinDivertSetParam(_handle, WinDivertNative.WINDIVERT_PARAM_QUEUE_SIZE, 33554432); // 32MB

        return true;
    }

    public void Close()
    {
        if (IsOpen)
        {
            WinDivertNative.WinDivertClose(_handle);
            _handle = nint.Zero;
        }
    }

    public bool Receive(Span<byte> buffer, out int length, out bool isOutbound)
    {
        length = 0;
        isOutbound = false;

        if (!IsOpen)
            return false;

        _lastAddress = default;
        var success = WinDivertNative.WinDivertRecv(
            _handle,
            buffer,
            (uint)buffer.Length,
            out var recvLen,
            ref _lastAddress);

        if (success)
        {
            length = (int)recvLen;
            isOutbound = _lastAddress.Outbound; // Now correctly reads bit 17
        }

        return success;
    }

    public bool Send(ReadOnlySpan<byte> packet, bool recalcChecksum)
    {
        if (!IsOpen)
            return false;

        if (recalcChecksum)
        {
            // Need mutable copy for checksum calculation
            var mutablePacket = new byte[packet.Length];
            packet.CopyTo(mutablePacket);
            
            WinDivertNative.WinDivertHelperCalcChecksums(
                mutablePacket,
                (uint)mutablePacket.Length,
                ref _lastAddress,
                0);
            
            return WinDivertNative.WinDivertSend(
                _handle,
                mutablePacket,
                (uint)mutablePacket.Length,
                out _,
                ref _lastAddress);
        }

        return WinDivertNative.WinDivertSend(
            _handle,
            packet,
            (uint)packet.Length,
            out _,
            ref _lastAddress);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Close();
        GC.SuppressFinalize(this);
    }

    ~WinDivertDriver()
    {
        Close();
    }
}
