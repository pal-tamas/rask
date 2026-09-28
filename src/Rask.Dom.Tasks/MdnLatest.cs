using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;

namespace Rask.Core.Dom.Build;

// The newest STABLE release of each source: npm's `latest` dist-tag (refused if a prerelease), and the head of
// webref's curated branch, which is webref's validated channel.
internal static class MdnLatest
{
    private const string NpmDistTags = "https://registry.npmjs.org/-/package/{0}/dist-tags";
    private const string WebrefCuratedHead = "https://api.github.com/repos/w3c/webref/commits/curated";

    public static IReadOnlyDictionary<string, string> Resolve()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("rask-build");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var package in new[] { "@mdn/browser-compat-data", "@webref/idl", "@webref/elements", "@webref/events", "webidl2" })
        {
            var url = string.Format(CultureInfo.InvariantCulture, NpmDistTags, package.Replace("/", "%2F"));
            var tags = DomEmitter.Parse(http.GetStringAsync(url).Result);
            var latest = tags["latest"]?.AsString() ?? throw new FormatException("no `latest` dist-tag for " + package);
            // Stable only: a `latest` that points at a prerelease (8.2.0-beta.1) is not taken; the committed
            // snapshot stays until a stable release follows it.
            if (latest.IndexOf('-') >= 0 || latest.IndexOf('+') >= 0)
            {
                throw new FormatException(package + "'s `latest` is the prerelease " + latest);
            }

            result[package] = latest;
        }

        var head = DomEmitter.Parse(http.GetStringAsync(WebrefCuratedHead).Result);
        result["webref/dfns"] = head["sha"]?.AsString() ?? throw new FormatException("no sha for webref's curated branch");

        return result;
    }
}
