using System.Net;
using System.Net.Sockets;

namespace Rask.Hosting.Shared;

/// <summary>
/// Where an anonymous caller connects from, as the key a quota or a throttle counts it under.
/// </summary>
internal static class ClientNetwork
{
    /// <summary>
    /// The caller's address — or, for IPv6, its <c>/64</c>: the smallest block a network hands out, so
    /// counting by full address would make one host 2^64 callers.
    /// </summary>
    public static string Of(IPAddress? address)
    {
        if (address is null)
        {
            return "unknown";
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return address.ToString();
        }

        Span<byte> bytes = stackalloc byte[16];
        address.TryWriteBytes(bytes, out _);
        bytes[8..].Clear();
        return new IPAddress(bytes).ToString();
    }
}
