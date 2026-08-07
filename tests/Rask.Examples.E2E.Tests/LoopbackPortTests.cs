using System.Net;
using System.Net.Sockets;
using Rask.Examples.E2E.Tests.Infrastructure;

namespace Rask.Examples.E2E.Tests;

public sealed class LoopbackPortTests
{
    [Fact]
    public void Reserve_hands_back_a_port_that_can_actually_be_bound()
    {
        var port = LoopbackPort.Reserve();

        // The family matters: a `localhost` prefix holds [::1] only where IPv6 is available, so a port
        // probed on the wrong family can look free and still refuse to bind (measured in #612).
        var loopback = Socket.OSSupportsIPv6 ? IPAddress.IPv6Loopback : IPAddress.Loopback;
        var listener = new TcpListener(loopback, port);
        listener.Start();
        listener.Stop();
    }

    [Fact]
    public void Reserve_does_not_hand_the_same_port_out_twice_in_a_row()
    {
        // Not a guarantee the OS makes in general, but if consecutive calls collided the retry loop in
        // ExampleAppFixture would burn all five attempts on the same number.
        var ports = Enumerable.Range(0, 8).Select(_ => LoopbackPort.Reserve()).ToArray();

        Assert.Equal(ports.Length, ports.Distinct().Count());
    }

    // Kestrel's real failure text. If this string ever drifts the retry silently stops working and a
    // clash comes back as "exited before becoming ready", so it is pinned verbatim rather than paraphrased.
    [Theory]
    [InlineData("Unhandled exception. System.IO.IOException: Failed to bind to address "
                + "http://localhost:5099: address already in use.")]
    [InlineData("System.Net.Sockets.SocketException (48): Address already in use")]
    [InlineData("Only one usage of each socket address (protocol/network address/port) is normally permitted.")]
    public void LooksLikeAddressInUse_recognises_a_bind_clash(string log) =>
        Assert.True(LoopbackPort.LooksLikeAddressInUse(log));

    // The other half of the contract, and the more important one: a host that died for its own reasons
    // must NOT be retried, or a genuinely broken sample turns a 120s timeout into a 600s one and the
    // reader is told the port was busy.
    [Theory]
    [InlineData("Unhandled exception. System.InvalidOperationException: Jwt__Key is not configured.")]
    [InlineData("Microsoft.Data.Sqlite.SqliteException: SQLite Error 14: 'unable to open database file'.")]
    [InlineData("")]
    public void LooksLikeAddressInUse_ignores_an_ordinary_crash(string log) =>
        Assert.False(LoopbackPort.LooksLikeAddressInUse(log));
}
