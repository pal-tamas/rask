using System.Globalization;
using System.Text;
using static Rask.Cli.Commands.DeployCommand;

namespace Rask.Cli.Commands;

/// <summary>Which color of which app the Caddy proxy routes each domain to, read from <c>docker ps</c> and written as a Caddyfile.</summary>
internal static class CaddyRouting
{
    /// <summary>The next blue-green color given the current one: nothing/green → blue, blue → green.</summary>
    internal static string NextColor(string? current) =>
        string.Equals(current, "blue", StringComparison.Ordinal) ? "green" : "blue";

    /// <summary>Parse the tab-separated <c>docker ps</c> label listing into deployed-app records.</summary>
    internal static IReadOnlyList<DeployedApp> ParseDeployedApps(string psOutput)
    {
        var apps = new List<DeployedApp>();
        foreach (var raw in psOutput.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0)
            {
                continue;
            }

            var parts = line.Split('\t');
            if (parts.Length < 4 || parts[0].Length == 0)
            {
                continue;
            }

            // A container deployed before rask.port existed reports no label; it can only have been
            // listening on the then-hardcoded default, so that is the correct reading of its absence.
            var port = parts.Length > 4 && int.TryParse(Label(parts[4]), NumberStyles.None, CultureInfo.InvariantCulture, out var labelled)
                ? labelled
                : DeployCommand.DefaultContainerPort;

            // rask.redirect came after rask.port: an older listing, or a container without redirects, has none.
            var redirects = parts.Length > 5 ? Label(parts[5]) : string.Empty;
            apps.Add(new DeployedApp(parts[0], Label(parts[1]), Label(parts[2]), Label(parts[3]), port, redirects));
        }

        return apps;

        // docker prints "<no value>" for a label a container doesn't carry.
        static string Label(string value) => string.Equals(value, "<no value>", StringComparison.Ordinal) ? string.Empty : value;
    }

    /// <summary>
    /// The <c>domain → container</c> routing for the shared proxy: every other app keeps its live
    /// container; the app being deployed is forced to its new container. Sorted for a deterministic file.
    /// </summary>
    internal static IReadOnlyDictionary<string, RouteTarget> BuildRoutingMap(
        IReadOnlyList<DeployedApp> apps, string deployingApp, string deployingDomain, RouteTarget newTarget)
    {
        var map = new SortedDictionary<string, RouteTarget>(StringComparer.Ordinal);
        foreach (var app in apps)
        {
            if (app.Domain.Length == 0 || string.Equals(app.App, deployingApp, StringComparison.Ordinal))
            {
                continue; // port-mode apps aren't proxied; the deploying app is set explicitly below
            }

            // Each app keeps ITS OWN container port: one box can host apps that don't agree on one. And its redirects.
            map[app.Domain] = new RouteTarget(app.Container, app.Port, app.Redirects);
        }

        map[deployingDomain] = newTarget;
        return map;
    }

    /// <summary>
    /// A host of <paramref name="deployingSite"/> that another app on the box already serves, with that app —
    /// or <c>null</c> when none is. Caddy refuses a config that names one host in two site blocks, and the
    /// reload would fail only after the new container had started; asked first, the deploy stops before it
    /// touches anything and says which app holds the name.
    /// </summary>
    internal static (string Host, string App, bool Redirected)? FindTakenHost(IReadOnlyList<DeployedApp> apps, string deployingApp, string deployingSite, string deployingRedirects = "")
    {
        // Served and redirected names alike: either way the host has a site block of its own in the shared file.
        var wanted = SiteDomains.Hosts(deployingSite).Concat(SiteDomains.Hosts(deployingRedirects)).ToList();
        foreach (var app in apps)
        {
            if (app.Domain.Length == 0 || string.Equals(app.App, deployingApp, StringComparison.Ordinal))
            {
                continue;
            }

            if (SiteDomains.Hosts(app.Domain).FirstOrDefault(host => wanted.Contains(host, StringComparer.OrdinalIgnoreCase)) is { } served)
            {
                return (served, app.App, false);
            }

            if (SiteDomains.Hosts(app.Redirects).FirstOrDefault(host => wanted.Contains(host, StringComparer.OrdinalIgnoreCase)) is { } redirected)
            {
                return (redirected, app.App, true);
            }
        }

        return null;
    }

    /// <summary>Whether any site or redirect in <paramref name="routes"/> names a wildcard host, whose certificate only DNS-01 can prove.</summary>
    internal static bool NeedsDnsChallenge(IReadOnlyDictionary<string, RouteTarget> routes) =>
        routes.Any(route => HasWildcard(route.Key) || HasWildcard(route.Value.Redirects));

    /// <summary>Whether a site string names a wildcard host (<c>*.example.com</c>).</summary>
    internal static bool HasWildcard(string site) =>
        SiteDomains.Hosts(site).Any(host => host.StartsWith("*.", StringComparison.Ordinal));

    /// <summary>
    /// Render the multi-site Caddyfile from a <c>domain → container</c> map (uses <c>\n</c> for determinism): a
    /// block per app, then a block per app's redirects. A block naming a wildcard host gets its certificate over DNS
    /// with <paramref name="dns"/>; every other block keeps Caddy's default HTTP-01, so with no wildcard anywhere the
    /// file is byte-for-byte what it always was.
    /// </summary>
    internal static string BuildCaddyfile(IReadOnlyDictionary<string, RouteTarget> routes, DnsProvider? dns = null)
    {
        var builder = new StringBuilder();
        foreach (var (domain, target) in routes)
        {
            // Every host of `domain` reached here through DomainName.TryParse, so none can close this block
            // early. An app on several domains is one block with all its addresses: `ppta.hu, www.ppta.hu {`.
            OpenBlock(builder, domain, dns);
            builder.Append("\treverse_proxy ").Append(target.Container).Append(':').Append(target.Port).Append('\n');
            builder.Append("}\n");
        }

        // After every served block, so a reader sees the apps first. The order does not decide anything: Caddy
        // matches the most specific address, so eszaf.hu and www.eszaf.hu above win over *.eszaf.hu here.
        foreach (var (domain, target) in routes.Where(route => route.Value.Redirects.Length > 0))
        {
            OpenBlock(builder, target.Redirects, dns);
            // 308 keeps the method and the body, so a program still POSTing to an old name arrives intact.
            builder.Append("\tredir https://").Append(SiteDomains.Primary(domain)).Append("{uri} 308\n");
            builder.Append("}\n");
        }

        return builder.ToString();

        static void OpenBlock(StringBuilder builder, string site, DnsProvider? dns)
        {
            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            builder.Append(SiteDomains.CaddyAddress(site)).Append(" {\n");
            if (dns is not null && HasWildcard(site))
            {
                builder.Append(dns.TlsBlock());
            }
        }
    }
}
