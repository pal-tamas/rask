using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Rask.Generators.Analyzers;

// One entry of <RaskBrowserTargets>: the oldest version of one engine's browser an app supports, as `safari >= 16`.
// `edge` is Chromium's: it shares Chrome's version numbers, which is how MDN's data reads for the generated members.
internal sealed class BrowserTarget
{
    private static readonly Dictionary<string, (string Engine, string Shown)> Browsers =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["chrome"] = ("Chrome", "Chrome"),
            ["edge"] = ("Chrome", "Edge"),
            ["firefox"] = ("Firefox", "Firefox"),
            ["safari"] = ("Safari", "Safari"),
        };

    private static readonly char[] Separators = { ';', ',' };

    private readonly string _engine;
    private readonly string _shown;
    private readonly int[] _version;
    private readonly string _versionText;

    private BrowserTarget(string engine, string shown, int[] version, string versionText)
    {
        _engine = engine;
        _shown = shown;
        _version = version;
        _versionText = versionText;
    }

    // The ';'- or ','-separated entries; what is not one comes back in `unread`, for RASK099.
    public static List<BrowserTarget> Parse(string text, out List<string> unread)
    {
        var targets = new List<BrowserTarget>();
        unread = new List<string>();
        foreach (var entry in text.Split(Separators, StringSplitOptions.RemoveEmptyEntries).Select(e => e.Trim()))
        {
            if (entry.Length == 0)
            {
                continue;
            }

            var at = entry.IndexOf(">=", StringComparison.Ordinal);
            var name = at < 0 ? "" : entry.Substring(0, at).Trim();
            var versionText = at < 0 ? "" : entry.Substring(at + 2).Trim();
            if (Browsers.TryGetValue(name, out var browser) && Version(versionText) is { } version)
            {
                targets.Add(new BrowserTarget(browser.Engine, browser.Shown, version, versionText));
            }
            else
            {
                unread.Add(entry);
            }
        }

        return targets;
    }

    // Why this target lacks the member, as the message shows it, or null when it ships there: "Safari >= 16 (never
    // shipped)", "Firefox >= 115 (from 120)".
    public string? Lacks(AttributeData support)
    {
        var added = support.NamedArguments
            .Where(a => string.Equals(a.Key, _engine, StringComparison.Ordinal))
            .Select(a => a.Value.Value as string)
            .FirstOrDefault();
        if (added is null)
        {
            return $"{_shown} >= {_versionText} (never shipped)";
        }

        return Version(added) is { } since && Compare(since, _version) > 0
            ? $"{_shown} >= {_versionText} (from {added})"
            : null;
    }

    private static int[]? Version(string text)
    {
        var parts = text.Split('.');
        var numbers = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
            {
                return null;
            }
        }

        return numbers;
    }

    private static int Compare(int[] a, int[] b)
    {
        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            var x = i < a.Length ? a[i] : 0;
            var y = i < b.Length ? b[i] : 0;
            if (x != y)
            {
                return x.CompareTo(y);
            }
        }

        return 0;
    }
}
