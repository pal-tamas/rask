using System.Globalization;
using static Rask.Cli.Commands.ProxyCommands;

namespace Rask.Cli.Commands;

/// <summary>
/// The check after the proxy is reloaded, before the old colour is retired: <c>caddy reload</c> only proves the
/// config parses, not that it routes anywhere real. A deploy is done when the proxy's running config sends the
/// primary domain to the new colour, and a request through the proxy reaches it.
/// </summary>
internal sealed partial class DeployCommand
{
    /// <summary>Retried across the readiness window; <c>null</c> when the route checks out, else what was seen last.</summary>
    private async Task<string?> VerifyRouteAsync(ProxySwitch change, CancellationToken cancellationToken)
    {
        var seen = "it was never checked";
        for (var attempt = 0; attempt < ReadinessAttempts; attempt++)
        {
            if (attempt > 0 && ReadinessDelay > TimeSpan.Zero)
            {
                await Task.Delay(ReadinessDelay, cancellationToken).ConfigureAwait(false);
            }

            if (await CheckRouteAsync(change, cancellationToken).ConfigureAwait(false) is not { } failure)
            {
                return null;
            }

            seen = failure;
        }

        return seen;
    }

    private async Task<string?> CheckRouteAsync(ProxySwitch change, CancellationToken cancellationToken)
    {
        var host = change.Host;
        var primary = SiteDomains.Primary(change.Domain);
        var expected = $"{change.Target.Container}:{change.Target.Port.ToString(CultureInfo.InvariantCulture)}";

        // What the proxy runs, from its admin API: the one upstream it sends the site to must be the new colour.
        var config = await Capture(BuildAdminConfigArguments(host), cancellationToken).ConfigureAwait(false);
        if (config.ExitCode != 0)
        {
            return $"its admin API did not answer ({config.StandardError.Trim()})";
        }

        var upstreams = CaddyRouteCheck.Upstreams(config.StandardOutput, primary);
        if (upstreams.Count == 0)
        {
            return $"its running config has no route for {primary}";
        }

        if (upstreams.Count > 1 || !string.Equals(upstreams[0], expected, StringComparison.Ordinal))
        {
            return $"its running config sends {primary} to {string.Join(", ", upstreams)}";
        }

        // ...and a request through it, as a visitor's arrives, reaches the app.
        var site = CaddyRouteCheck.ProbeHost(primary);
        var path = change.HealthEnabled ? change.HealthPath : "/";
        var through = await Capture(BuildThroughProxyArguments(host, site, path), cancellationToken).ConfigureAwait(false);
        if (through.ExitCode == 0)
        {
            return CaddyRouteCheck.Answered(through.StandardOutput, change.HealthEnabled)
                ? null
                : $"https://{site}{path} answered {through.StandardOutput.Trim()} through it";
        }

        if (through.ExitCode != CaddyRouteCheck.TlsHandshakeFailed)
        {
            return $"https://{site}{path} could not be fetched through it (curl exit {through.ExitCode.ToString(CultureInfo.InvariantCulture)})";
        }

        // No certificate yet: the DNS doesn't point here, or the ACME order is still pending. The route is then
        // checked one step in — the upstream it names, asked from the proxy's own network, has to answer.
        var upstream = await Capture(BuildUpstreamFromProxyArguments(host, expected, path, change.HealthEnabled), cancellationToken).ConfigureAwait(false);
        if (upstream.ExitCode != 0)
        {
            return $"{expected} did not answer {path} from the proxy's network";
        }

        Console.WriteLine($"  No certificate for {site} yet, so the route was checked at its upstream: {expected} answers the proxy.", ConsoleStyle.Dim);
        return null;
    }
}
