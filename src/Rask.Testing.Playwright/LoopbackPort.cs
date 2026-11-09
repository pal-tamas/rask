using System.Net;
using System.Net.Sockets;

namespace Rask.Testing;

/// <summary>A free loopback port, asked of the OS, for an app that has to be told where to listen.</summary>
/// <remarks>
///     The probe binds the family <c>localhost</c> resolves to first (IPv6 where the machine has it), so the number is
///     free for the host about to bind it. The probe is released before the host binds, which leaves a window another
///     process can take the port in: an out-of-process host has to be told its port before it listens. The caller
///     closes it by starting again on a new port when the host says the address was in use
///     (<see cref="LooksLikeAddressInUse" />).
/// </remarks>
internal static class LoopbackPort
{
    // The OS may hand a just-released port straight back, so two sites asked for in a row could get one number.
    private static readonly HashSet<int> HandedOut = [];
    private static readonly Lock Gate = new();

    /// <summary>A port the OS has just confirmed free, and not handed to another site in this process.</summary>
    public static int Reserve()
    {
        lock (Gate)
        {
            for (var attempt = 0; attempt < 64; attempt++)
            {
                var port = Probe();
                if (HandedOut.Add(port))
                {
                    return port;
                }
            }

            throw new InvalidOperationException("The OS kept handing back ports this test run already gave out.");
        }
    }

    /// <summary>Whether a host's output is Kestrel's "the port is taken" rather than the app failing.</summary>
    public static bool LooksLikeAddressInUse(string output) =>
        output.Contains("address already in use", StringComparison.OrdinalIgnoreCase)
        || output.Contains("Failed to bind to address", StringComparison.OrdinalIgnoreCase)
        || output.Contains("Only one usage of each socket address", StringComparison.OrdinalIgnoreCase);

    private static int Probe()
    {
        var loopback = Socket.OSSupportsIPv6 ? IPAddress.IPv6Loopback : IPAddress.Loopback;
        var probe = new TcpListener(loopback, 0);
        probe.Start();
        try
        {
            return ((IPEndPoint)probe.LocalEndpoint).Port;
        }
        finally
        {
            probe.Stop();
        }
    }
}
