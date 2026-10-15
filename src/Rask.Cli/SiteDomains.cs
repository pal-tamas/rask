namespace Rask.Cli;

/// <summary>
/// The host names one app is served on — every <c>--domain</c> given, in order, the first being the primary.
///
/// <para>Carried through a deploy as one canonical string, the hosts joined by <c>,</c>
/// (<c>ppta.hu,www.ppta.hu</c>): that is the <c>rask.domain</c> label on the app's container, read back from
/// <c>docker ps</c> to regenerate the proxy's Caddyfile, so a box deployed by an older Rask — one host, no
/// comma — reads exactly as before. A comma can't occur inside a host (<see cref="DomainName.TryParse"/>
/// admits letters, digits, <c>-</c>, <c>.</c> and a leading <c>*.</c>), so the join is unambiguous.</para>
/// </summary>
internal static class SiteDomains
{
    private const char Separator = ',';

    /// <summary>
    /// Validate every <c>--domain</c> value — each may itself be a comma-separated list — and join them into
    /// the canonical site string. Each host goes through <see cref="DomainName.TryParse"/>, the security
    /// boundary between this input and the proxy config every app on the box shares; a host given twice is
    /// refused rather than quietly dropped, since it is far more likely a typo than an intent.
    /// </summary>
    public static bool TryParse(IEnumerable<string> values, out string? site, out string? error)
    {
        site = null;
        error = null;
        var hosts = new List<string>();
        foreach (var value in values)
        {
            foreach (var part in value.Split(Separator))
            {
                if (!DomainName.TryParse(part, out var host, out error))
                {
                    return false;
                }

                // DNS names compare without case; the proxy and the certificate do too.
                host = host.ToLowerInvariant();
                if (hosts.Contains(host, StringComparer.Ordinal))
                {
                    error = $"'{host}' is given more than once — each domain once is enough.";
                    return false;
                }

                hosts.Add(host);
            }
        }

        if (hosts.Count > 0)
        {
            site = string.Join(Separator, hosts);
        }

        return true;
    }

    /// <summary>The hosts of a site string (or of a <c>rask.domain</c> label), in order.</summary>
    public static IReadOnlyList<string> Hosts(string site) =>
        site.Split(Separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>The host the app names itself by: the first one given.</summary>
    public static string Primary(string site) => Hosts(site) is [var first, ..] ? first : site;

    /// <summary>The site's address line in a Caddyfile: <c>ppta.hu, www.ppta.hu</c>.</summary>
    public static string CaddyAddress(string site) => string.Join(", ", Hosts(site));

    /// <summary>A site for people to read: the primary host, and how many more there are.</summary>
    public static string Describe(string site)
    {
        var hosts = Hosts(site);
        return hosts.Count > 1 ? $"{hosts[0]} (+ {string.Join(", ", hosts.Skip(1))})" : Primary(site);
    }
}
