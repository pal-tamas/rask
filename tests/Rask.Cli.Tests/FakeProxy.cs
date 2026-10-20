using System.Text.Json;
using Rask.Cli.Commands;

namespace Rask.Cli.Tests;

/// <summary>
/// What the shared proxy answers a deploy's check through it, for the scripted-runner tests: the admin API reports
/// the routes of the proxy files the deploy wrote, and a request through the proxy succeeds. A test about a failing
/// check sets its own capture handler after the command is created, which replaces this one.
/// </summary>
internal static class FakeProxy
{
    /// <summary>Answer the proxy-check captures from what <paramref name="fs"/> saw written, and everything else as before.</summary>
    public static void Wire(FakeProcessRunner runner, FakeFileSystem fs)
    {
        var scripted = runner.CaptureHandler;
        if (scripted is null)
        {
            return;
        }

        runner.CaptureHandler = args => Answer(args, fs) ?? scripted(args);
    }

    /// <summary>Every proxy file the deploy wrote, in order — copied to the box and then deleted, so read from the write history.</summary>
    public static IEnumerable<string> WrittenFiles(FakeFileSystem fs) =>
        fs.Written.Where(file => file.Key.EndsWith(CaddySites.FragmentExtension, StringComparison.Ordinal)).Select(file => file.Value);

    /// <summary>
    /// The JSON Caddy's admin API returns for these Caddyfiles, in the shape <c>caddy adapt</c> gives a site block: a
    /// route matching the site's hosts, handled by a subroute whose reverse_proxy names the upstream. A later file's
    /// site replaces an earlier one's, as a rewritten file does.
    /// </summary>
    public static string AdminConfig(IEnumerable<string> caddyfiles)
    {
        var routes = Sites(caddyfiles).Select(site => new Dictionary<string, object>
        {
            ["match"] = new[] { new Dictionary<string, object> { ["host"] = site.Key.Split(", ") } },
            ["handle"] = new[]
            {
                new Dictionary<string, object>
                {
                    ["handler"] = "subroute",
                    ["routes"] = new[]
                    {
                        new Dictionary<string, object>
                        {
                            ["handle"] = site.Value is null
                                ? new object[] { new Dictionary<string, object> { ["handler"] = "static_response", ["status_code"] = 308 } }
                                : [new Dictionary<string, object> { ["handler"] = "reverse_proxy", ["upstreams"] = new[] { new Dictionary<string, object> { ["dial"] = site.Value } } }],
                        },
                    },
                },
            },
            ["terminal"] = true,
        });

        var config = new Dictionary<string, object>
        {
            ["apps"] = new Dictionary<string, object>
            {
                ["http"] = new Dictionary<string, object>
                {
                    ["servers"] = new Dictionary<string, object> { ["srv0"] = new Dictionary<string, object> { ["listen"] = new[] { ":443" }, ["routes"] = routes.ToArray() } },
                },
            },
        };

        return JsonSerializer.Serialize(config);
    }

    /// <summary>
    /// The site blocks of these Caddyfiles, as <c>"a, b" → "container:port"</c> (null for a redirect). A later
    /// file's site replaces an earlier one's, as a rewritten file does.
    /// </summary>
    public static IReadOnlyDictionary<string, string?> Sites(IEnumerable<string> caddyfiles)
    {
        var sites = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var caddyfile in caddyfiles)
        {
            string? address = null;
            string? upstream = null;
            foreach (var line in caddyfile.Split('\n'))
            {
                if (line.EndsWith(" {", StringComparison.Ordinal) && !line.StartsWith('\t'))
                {
                    address = line[..^2];
                    upstream = null;
                }
                else if (line.Trim().StartsWith("reverse_proxy ", StringComparison.Ordinal))
                {
                    upstream = line.Trim()["reverse_proxy ".Length..];
                }
                else if (string.Equals(line, "}", StringComparison.Ordinal) && address is not null)
                {
                    sites[address] = upstream;
                    address = null;
                }
            }
        }

        return sites;
    }

    private static ProcessResult? Answer(IReadOnlyList<string> args, FakeFileSystem fs) =>
        args.Contains(ProxyCommands.AdminConfigUrl) ? new ProcessResult(0, AdminConfig(WrittenFiles(fs)), string.Empty)
        : args.Contains("--resolve") ? new ProcessResult(0, "200", string.Empty)
        : null;
}
