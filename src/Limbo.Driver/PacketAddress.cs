using Limbo.Driver.Native;

namespace Limbo.Driver;

/// <summary>
/// Capture metadata of one received packet (direction, network interface). Opaque on
/// purpose: copy it from <see cref="IPacketDriver.LastAddress"/> right after Receive,
/// then use it to reinject or answer that packet later, possibly from another thread,
/// after further packets have been received.
/// </summary>
public readonly struct PacketAddress
{
    internal WinDivertAddress Raw { get; }

    internal PacketAddress(WinDivertAddress raw) => Raw = raw;

    public bool IsOutbound => Raw.Outbound;
}
