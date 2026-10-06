using System.Net;
using System.Net.Sockets;

namespace Rask.WebPush;

/// <summary>
/// How the sender reaches a push service: no redirects, and only ever a public address.
/// </summary>
/// <remarks>
/// A stored endpoint is a URL somebody posted to an open route, and a send is this server's own POST to it.
/// Refusing an address or <c>localhost</c> when the subscription is stored checks the NAME; a name can
/// still resolve to the metadata service or a host on the private network. So the address is checked where
/// it is finally known — at connect, after resolution, which is also what defeats a name that resolves
/// differently the second time it is asked.
/// </remarks>
internal static class PushConnection
{
    /// <summary>The handler both registrations of the sender use.</summary>
    public static SocketsHttpHandler Handler() => new()
    {
        // A push service never redirects, and following one would let whoever owns a stored endpoint
        // point the POST somewhere the subscription check never saw.
        AllowAutoRedirect = false,
        ConnectCallback = ConnectAsync,
    };

    /// <summary>Whether <paramref name="address" /> is one a push service could be listening on.</summary>
    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return !(IPAddress.IsLoopback(address)
                     || address.Equals(IPAddress.IPv6None)
                     || address.Equals(IPAddress.IPv6Any)
                     || address.IsIPv6LinkLocal
                     || address.IsIPv6SiteLocal
                     || address.IsIPv6UniqueLocal
                     || address.IsIPv6Multicast);
        }

        Span<byte> b = stackalloc byte[4];
        address.TryWriteBytes(b, out _);

        return b[0] switch
        {
            0 or 10 or 127 => false,                // this network, private, loopback
            100 => b[1] is < 64 or > 127,           // 100.64/10, carrier-grade NAT
            169 => b[1] != 254,                     // link-local, where cloud metadata lives
            172 => b[1] is < 16 or > 31,            // private
            192 => b[1] != 168 && !(b[1] == 0 && b[2] == 0), // private, and 192.0.0/24 protocol assignments
            198 => b[1] is not (18 or 19),          // benchmarking
            >= 224 => false,                        // multicast and reserved
            _ => true,
        };
    }

    private static async ValueTask<Stream> ConnectAsync(
        SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;
        var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);

        // Behind an egress proxy this connection is to the PROXY, which is expected to be on the private
        // network and resolves the push service itself — so there is no address here to judge, and
        // refusing would stop every send. Where traffic leaves through a proxy, the proxy is the place
        // that says where it may go.
        var direct = string.Equals(
            host, context.InitialRequestMessage.RequestUri?.IdnHost, StringComparison.OrdinalIgnoreCase);
        var reachable = direct ? Array.FindAll(addresses, IsPublic) : addresses;

        if (reachable.Length == 0)
        {
            throw new HttpRequestException(
                $"'{host}' does not resolve to a public address, so it is not a push service.");
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            // The checked addresses themselves, never the name again: a second lookup could answer differently.
            await socket.ConnectAsync(reachable, context.DnsEndPoint.Port, cancellationToken).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
