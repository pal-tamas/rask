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

            apps.Add(new DeployedApp(parts[0], Label(parts[1]), Label(parts[2]), Label(parts[3]), port));
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

            // Each app keeps ITS OWN container port: one box can host apps that don't agree on one.
            map[app.Domain] = new RouteTarget(app.Container, app.Port);
        }

        map[deployingDomain] = newTarget;
        return map;
    }

    /// <summary>Render the multi-site Caddyfile from a <c>domain → container</c> map (uses <c>\n</c> for determinism).</summary>
    internal static string BuildCaddyfile(IReadOnlyDictionary<string, RouteTarget> routes)
    {
        var builder = new StringBuilder();
        foreach (var (domain, target) in routes)
        {
            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            // `domain` reached here through DomainName.TryParse, so it cannot close this block early.
            builder.Append(domain).Append(" {\n");
            builder.Append("\treverse_proxy ").Append(target.Container).Append(':').Append(target.Port).Append('\n');
            builder.Append("}\n");
        }

        return builder.ToString();
    }
}
