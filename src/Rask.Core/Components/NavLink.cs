using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;
using Rask.Core.Routing;

namespace Rask.Core.Components;

/// <summary>
///     An <c>a</c> that knows whether it points at the current page, and adds <c>ActiveClass</c> when it
///     does. Takes a type-safe <c>RouteUrl</c> rather than a string, so a renamed route breaks the build
///     instead of the link.
/// </summary>
public sealed class NavLink : Element
{
    // Cached at mount because LiveRenderContext.Current is null during disposal, so
    // OnUnmount can't re-resolve RouteState from the render scope.
    private RouteState? _route;
    protected override string TagName => "a";

    /// <summary>
    ///     Where the link goes, as a generated route URL — <c>Routes.Orders()</c> rather than
    ///     <c>"/orders"</c>.
    /// </summary>
    public RouteUrl? Href { get; set; }

    // The path the active state is compared against, when it differs from Href. Use it for a link that
    // should stay active across a whole section: Href the canonical landing route (e.g. /realtime/BTC)
    // and Match the section root (/realtime) with ActiveMatch: Prefix. Null falls back to Href.

    /// <summary>
    ///     The URL to compare against the current location, when that differs from <c>Href</c>. Defaults to
    ///     <c>Href</c>.
    /// </summary>
    public RouteUrl? Match { get; set; }

    // null defaults to "active" at use site so the generated factory exposes ActiveClass
    // as a normal optional parameter — properties with initializers are excluded by the
    // factory generator. To opt out of active styling, pass an empty string.

    /// <summary>The class added while the link is active. Defaults to <c>active</c>.</summary>
    public string? ActiveClass { get; set; }

    // Optional: null is treated as Exact (the default match mode).

    /// <summary>
    ///     How exactly the URL must match to count as active: the whole path, or any prefix of it.
    /// </summary>
    public NavLinkMatch? ActiveMatch { get; set; }

    protected override void OnMount()
    {
        // Subscribe to RouteState.Changed so a NavLink rendered outside the Router
        // subtree (e.g. a top-level sidebar in App.cs) still re-evaluates its active
        // state when navigation happens.
        _route = LiveRenderContext.Current?.Services?.GetService<RouteState>();
        if (_route is null)
        {
            return;
        }

        _route.Changed += StateHasChanged;
    }

    protected override void OnUnmount()
    {
        if (_route is null)
        {
            return;
        }

        _route.Changed -= StateHasChanged;
    }

    protected override string? ResolveClass()
    {
        // Null ActiveClass → default "active". Empty string → user opted out of active styling.
        var active = ActiveClass ?? "active";
        if (!IsActive() || string.IsNullOrEmpty(active))
        {
            return Class;
        }

        return string.IsNullOrEmpty(Class) ? active : $"{Class} {active}";
    }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Href is not null)
        {
            AppendUrlAttr(sb, "href", Href.Value.ToString());
        }

        AppendAttr(sb, "data-rask-nav", null);
    }

    private bool IsActive()
    {
        // Match overrides Href for the active comparison (link one place, light up for a section).
        var target = Match ?? Href;
        if (target is null)
        {
            return false;
        }

        var route = LiveRenderContext.Current?.Services?.GetService<RouteState>();
        if (route is null)
        {
            return false;
        }

        return (ActiveMatch ?? NavLinkMatch.Exact) switch
        {
            NavLinkMatch.Exact => MatchExact(route, target.Value.Path, target.Value.QueryString),
            NavLinkMatch.Prefix => MatchPrefix(route.Path, target.Value.Path),
            _ => false
        };
    }

    private static bool MatchExact(RouteState route, string hrefPath, string? hrefQuery)
    {
        if (!PathsEqual(route.Path, hrefPath))
        {
            return false;
        }

        if (string.IsNullOrEmpty(hrefQuery))
        {
            return true;
        }

        foreach (var pair in ParseQuery(hrefQuery))
        {
            if (!route.Query.TryGetValue(pair.Key, out var values))
            {
                return false;
            }

            if (!values.Contains(pair.Value))
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchPrefix(string currentPath, string hrefPath)
    {
        var cur = TrimSlash(currentPath);
        var pre = TrimSlash(hrefPath);
        if (pre.Length == 0)
        {
            return true;
        }

        if (cur.Equals(pre, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return cur.Length > pre.Length
               && cur.StartsWith(pre, StringComparison.OrdinalIgnoreCase)
               && cur[pre.Length] == '/';
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(TrimSlash(a), TrimSlash(b), StringComparison.OrdinalIgnoreCase);

    private static string TrimSlash(string s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return string.Empty;
        }

        return s.Length > 1 && s[^1] == '/' ? s[..^1] : s;
    }

    private static IEnumerable<KeyValuePair<string, string>> ParseQuery(string qs)
    {
        var s = qs.StartsWith('?') ? qs[1..] : qs;
        if (s.Length == 0)
        {
            yield break;
        }

        foreach (var part in s.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq < 0)
            {
                yield return new KeyValuePair<string, string>(Uri.UnescapeDataString(part), string.Empty);
            }
            else
            {
                yield return new KeyValuePair<string, string>(
                    Uri.UnescapeDataString(part[..eq]),
                    Uri.UnescapeDataString(part[(eq + 1)..]));
            }
        }
    }
}
