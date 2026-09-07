using System.Globalization;

namespace Rask.Cli.Dev;

/// <summary>
///     Renders the two files <c>rask dev</c> asks the machine for: the <c>/etc/hosts</c> block that
///     points the dev hostname at loopback, and the pf anchor that puts the app on ports 80/443.
/// </summary>
/// <remarks>
///     <para>
///         Both are pure string functions, and both are written to return <c>null</c> when the file
///         already says what it should. That is the load-bearing property of this whole feature: the
///         only way `rask dev` earns the right to be automatic is by asking for sudo <em>once</em> and
///         never again, and "never again" is decided here rather than by remembering that we asked.
///         Remembering would go stale the moment someone edited <c>/etc/hosts</c> by hand; recomputing
///         cannot.
///     </para>
///     <para>
///         Everything is written inside a marked block so it can be found again and removed cleanly.
///         Nothing outside the markers is ever rewritten — <c>/etc/hosts</c> belongs to the developer
///         and may well contain entries that matter more than ours.
///     </para>
/// </remarks>
internal static class DevHostFiles
{
    /// <summary>Opens the region of <c>/etc/hosts</c> this tool owns.</summary>
    public const string HostsBeginMarker = "# >>> rask dev >>>";

    /// <summary>Closes the region of <c>/etc/hosts</c> this tool owns.</summary>
    public const string HostsEndMarker = "# <<< rask dev <<<";

    /// <summary>
    ///     The pf anchor <c>rask dev</c> loads its redirects into.
    /// </summary>
    /// <remarks>
    ///     Nested under <c>com.apple</c> on purpose. macOS ships an <c>/etc/pf.conf</c> containing
    ///     <c>rdr-anchor "com.apple/*"</c>, so an anchor loaded here is evaluated without editing
    ///     <c>pf.conf</c> at all. That matters: the alternative — writing our own ruleset with
    ///     <c>pfctl -f</c> — would *replace* whatever firewall rules the developer already had, and
    ///     adding a line to <c>pf.conf</c> would be undone by the next OS update anyway.
    /// </remarks>
    public const string PfAnchor = "com.apple/rask";

    /// <summary>
    ///     <paramref name="hosts" /> with <paramref name="hostname" /> present in the managed block, or
    ///     null when it is already there and nothing needs writing.
    /// </summary>
    /// <remarks>
    ///     Only an IPv4 record is written. The pf redirect below is <c>inet</c>, and Kestrel is bound to
    ///     <c>127.0.0.1</c>, so publishing a <c>::1</c> record too would hand the browser an address
    ///     that nothing answers on and turn "which family did it try first" into an intermittent
    ///     connection failure.
    /// </remarks>
    public static string? AddHost(string hosts, string hostname)
    {
        ArgumentNullException.ThrowIfNull(hosts);
        ArgumentException.ThrowIfNullOrWhiteSpace(hostname);

        var existing = ManagedHosts(hosts);
        if (existing.Contains(hostname, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        var names = new List<string>(existing) { hostname };
        names.Sort(StringComparer.Ordinal);

        var block = RenderHostsBlock(names);
        var (start, end) = FindBlock(hosts);

        if (start < 0)
        {
            var separator = hosts.Length == 0 || hosts.EndsWith('\n') ? string.Empty : "\n";
            return hosts + separator + "\n" + block;
        }

        return hosts[..start] + block + hosts[end..];
    }

    /// <summary>
    ///     <paramref name="hosts" /> with the whole managed block removed, or null when there was none.
    ///     The undo half; nothing outside the markers is touched.
    /// </summary>
    public static string? RemoveBlock(string hosts)
    {
        ArgumentNullException.ThrowIfNull(hosts);

        var (start, end) = FindBlock(hosts);
        if (start < 0)
        {
            return null;
        }

        // The blank line we inserted ahead of the block goes with it, so repeated add/remove cycles
        // cannot slowly grow a run of empty lines in the middle of someone's hosts file.
        while (start >= 2 && hosts[start - 1] == '\n' && hosts[start - 2] == '\n')
        {
            start--;
        }

        return hosts[..start] + hosts[end..];
    }

    /// <summary>The hostnames currently listed in the managed block, in file order.</summary>
    public static IReadOnlyList<string> ManagedHosts(string hosts)
    {
        ArgumentNullException.ThrowIfNull(hosts);

        var (start, end) = FindBlock(hosts);
        if (start < 0)
        {
            return [];
        }

        var names = new List<string>();
        foreach (var raw in hosts[start..end].Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            // "127.0.0.1 appname.test" — anything after the address is a name for it.
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (var i = 1; i < parts.Length; i++)
            {
                if (!names.Contains(parts[i], StringComparer.OrdinalIgnoreCase))
                {
                    names.Add(parts[i]);
                }
            }
        }

        return names;
    }

    /// <summary>
    ///     The pf ruleset that puts the running app on port 443, or null when it is already listening
    ///     there and there is nothing to redirect.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Only 443. The dev host is HTTPS end to end — there is no plaintext listener to point port
    ///         80 at, and mapping it to the TLS port would answer a plain HTTP request with a TLS
    ///         handshake, which reads to the browser as a connection that broke rather than one that
    ///         should have been <c>https://</c>.
    ///     </para>
    ///     <para>
    ///         Scoped to <c>lo0</c> and to a <c>127.0.0.1</c> destination, so this only ever affects
    ///         traffic the developer's own machine sends to itself. It cannot expose the dev app to the
    ///         network, which a rule on the real interface would.
    ///     </para>
    ///     <para>
    ///         pf redirects on address and port; it cannot see a hostname, let alone a TLS SNI. So port
    ///         443 belongs to exactly one app at a time, and the anchor is rewritten to point at
    ///         whichever <c>rask dev</c> is currently running.
    ///     </para>
    /// </remarks>
    public static string? PfRules(int httpsPort) =>
        httpsPort == 443
            ? null
            // pfctl rejects a ruleset that does not end in a newline.
            : string.Create(
                CultureInfo.InvariantCulture,
                $"rdr pass on lo0 inet proto tcp from any to 127.0.0.1 port 443 -> 127.0.0.1 port {httpsPort}\n");

    private static string RenderHostsBlock(IReadOnlyList<string> names)
    {
        var lines = new List<string>
        {
            HostsBeginMarker,
            "# Added by `rask dev`. Delete this block to undo; it is rewritten, never duplicated.",
        };

        lines.AddRange(names.Select(name => "127.0.0.1\t" + name));
        lines.Add(HostsEndMarker);

        return string.Join('\n', lines) + "\n";
    }

    /// <summary>
    ///     The half-open character range covering the managed block including its trailing newline, or
    ///     <c>(-1, -1)</c> when the file has no such block.
    /// </summary>
    private static (int Start, int End) FindBlock(string hosts)
    {
        var start = hosts.IndexOf(HostsBeginMarker, StringComparison.Ordinal);
        if (start < 0)
        {
            return (-1, -1);
        }

        var end = hosts.IndexOf(HostsEndMarker, start, StringComparison.Ordinal);
        if (end < 0)
        {
            // An opened block with no close: a half-written file, or someone deleted the end marker.
            // Treat the rest of the file as the block rather than corrupting it further by appending a
            // second one — the caller rewrites it whole.
            return (start, hosts.Length);
        }

        end += HostsEndMarker.Length;
        if (end < hosts.Length && hosts[end] == '\n')
        {
            end++;
        }

        return (start, end);
    }
}
