using System.Net;
using System.Net.Sockets;

namespace Rask.Examples.E2E.Tests.Infrastructure;

/// <summary>
///     Asks the OS for a free loopback port, so nothing in this suite has to keep a list of numbers unique
///     by hand.
/// </summary>
/// <remarks>
///     <para>
///         Every fixture used to declare a constant. That was unique <em>within</em> a run, which is all the
///         comments claimed, but each copy of the suite on the machine claimed the same numbers — so a
///         straggler host, or a second worktree mid-suite, produced either a bind failure or, worse, a poll
///         that succeeded against somebody else's process. <c>5099</c> was the sharpest case: the
///         <c>pre-push</c> hook runs its gate on it, so one leftover host blocked pushing from every
///         worktree on the machine.
///     </para>
///     <para>
///         The probe binds the family <c>localhost</c> will actually resolve to. A <c>localhost</c> prefix
///         holds <c>[::1]</c> only where IPv6 is available (measured in #612 — which is also why a port can
///         look free on <c>127.0.0.1</c> and still refuse to bind), so probing the wrong family hands back a
///         number that is not free for the thing about to use it.
///     </para>
///     <para>
///         Releasing the probe before the real listener binds leaves a window in which someone else can take
///         the port. That is unavoidable here: an <see cref="System.Diagnostics.Process">out-of-process</see>
///         host has to be <em>told</em> where to listen, so the number must be decided before anything binds
///         — the "let <c>HttpListener.Start()</c> be the authoritative test" trick that fixed the in-process
///         static hosts does not transfer. Callers close the window by retrying: see
///         <see cref="LooksLikeAddressInUse" />.
///     </para>
/// </remarks>
internal static class LoopbackPort
{
    /// <summary>Returns a port the OS has just confirmed free on the loopback family <c>localhost</c> uses.</summary>
    public static int Reserve()
    {
        var loopback = Socket.OSSupportsIPv6 ? IPAddress.IPv6Loopback : IPAddress.Loopback;
        var probe = new TcpListener(loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    /// <summary>
    ///     True when a host's drained output is the "someone already has that port" failure rather than the
    ///     app being broken. Without this the two are indistinguishable: a child that fails to bind exits
    ///     immediately, and the fixture reported that as "exited before becoming ready", sending the reader
    ///     after a bug in the sample.
    /// </summary>
    /// <remarks>
    ///     Kestrel fails fast with <c>IOException: Failed to bind to address http://localhost:{port}:
    ///     address already in use.</c> Both fragments are matched: the second is the OS text and can be
    ///     localised, the first is Kestrel's own and is not.
    /// </remarks>
    public static bool LooksLikeAddressInUse(string log) =>
        log.Contains("address already in use", StringComparison.OrdinalIgnoreCase)
        || log.Contains("Failed to bind to address", StringComparison.OrdinalIgnoreCase)
        || log.Contains("Only one usage of each socket address", StringComparison.OrdinalIgnoreCase);
}
