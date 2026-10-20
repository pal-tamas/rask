using System.Globalization;
using System.Text.Json;

namespace Rask.Cli.Commands;

/// <summary>
/// Reads what the running proxy does with a site: the upstreams its live config sends the site to (from the admin
/// API's JSON), and whether a status code that came back through it counts as the app answering.
/// </summary>
internal static class CaddyRouteCheck
{
    /// <summary>curl's exit code for a TLS handshake that failed — what a site whose certificate isn't issued yet gives.</summary>
    internal const int TlsHandshakeFailed = 35;

    /// <summary>
    /// Every <c>dial</c> address under the routes that match <paramref name="site"/> by host, in the config JSON
    /// Caddy's admin API returns. Empty when no route names the site, or when the JSON can't be read.
    /// </summary>
    internal static IReadOnlyList<string> Upstreams(string configJson, string site)
    {
        try
        {
            using var config = JsonDocument.Parse(configJson);
            var dials = new List<string>();
            Collect(config.RootElement, site, matched: false, dials);
            return [.. dials.Distinct(StringComparer.Ordinal)];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// Whether a status code from the app, through the proxy, means it answered. A health path has to succeed. With the
    /// health check off any answer of the app's own counts — only the proxy's own failures (502, 503, 504) do not.
    /// </summary>
    internal static bool Answered(string statusOutput, bool healthEnabled)
    {
        if (!int.TryParse(statusOutput.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var status) || status is < 100 or > 599)
        {
            return false;
        }

        return healthEnabled ? status is >= 200 and < 400 : status is not (502 or 503 or 504);
    }

    /// <summary>The name to ask for a site by: a wildcard site is asked for under a name of its own.</summary>
    internal static string ProbeHost(string primary) =>
        primary.StartsWith("*.", StringComparison.Ordinal) ? "rask-check" + primary[1..] : primary;

    private static void Collect(JsonElement element, string site, bool matched, List<string> dials)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                Collect(item, site, matched, dials);
            }

            return;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var here = matched || MatchesHost(element, site);
        foreach (var property in element.EnumerateObject())
        {
            if (here && property.NameEquals("dial") && property.Value.ValueKind == JsonValueKind.String)
            {
                dials.Add(property.Value.GetString()!);
            }
            else
            {
                Collect(property.Value, site, here, dials);
            }
        }
    }

    // A route's "match" is a list of matcher sets; Caddy adapts a site address into one with a "host" list.
    private static bool MatchesHost(JsonElement route, string site) =>
        route.TryGetProperty("match", out var match)
        && match.ValueKind == JsonValueKind.Array
        && match.EnumerateArray().Any(set =>
            set.ValueKind == JsonValueKind.Object
            && set.TryGetProperty("host", out var hosts)
            && hosts.ValueKind == JsonValueKind.Array
            && hosts.EnumerateArray().Any(host =>
                host.ValueKind == JsonValueKind.String && string.Equals(host.GetString(), site, StringComparison.OrdinalIgnoreCase)));
}
