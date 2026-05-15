using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;
using Rask.Core.Routing;

namespace Rask.Core.Components;

// Headless route-aware link. NavLink renders nothing of its own — the caller's Template
// owns the markup and decides which element to render, how to style the active state,
// and where to attach children. NavLink supplies the computed state and the data
// attributes the runtime needs for SPA navigation:
//
//   NavLink(Href: "/foo", OnClick: CloseDrawer,
//       Template: s => A(Href: s.Href, Data: s.NavAttrs,
//                        Class: s.IsActive ? "active" : "")[
//           "Dashboard"
//       ])
//
// The template MUST splat `state.NavAttrs` onto an <a> for client-side SPA navigation
// to work (the JS listener targets `a[data-rask-nav]`). Render the attrs on something
// else and the link will full-page-navigate.
public sealed class NavLink : Component
{
    public RouteUrl? Href { get; set; }

    // default(NavLinkMatch) == Exact (enum's 0th value), so no initializer needed.
    public NavLinkMatch ActiveMatch { get; set; }

    // Optional handler invoked alongside the navigation. The client runtime coalesces
    // NavLink clicks with a co-located OnClick into a single ordered navigate message
    // (clickId field) so the handler runs server-side before the route change is
    // applied — one re-render, no flicker.
    public Action? OnClick { get; set; }
    public Func<Task>? OnClickAsync { get; set; }

    public required Func<NavLinkState, Component> Template { get; set; }

    // Cached at mount because LiveRenderContext.Current is null during disposal, so
    // OnUnmount can't re-resolve RouteState from the render scope.
    private RouteState? _route;

    protected override void OnMount()
    {
        _route = LiveRenderContext.Current?.Services?.GetService<RouteState>();
        if (_route is null) return;
        _route.Changed += StateHasChanged;
    }

    protected override void OnUnmount()
    {
        if (_route is null) return;
        _route.Changed -= StateHasChanged;
    }

    protected override Component Render()
    {
        var navAttrs = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["rask-nav"] = null
        };

        var click = (Delegate?)OnClick ?? OnClickAsync;
        if (click is not null && LiveRenderContext.Current is { } ctx)
        {
            navAttrs["rask-on-click"] = ctx.RegisterHandler(click);
        }

        var state = new NavLinkState(
            IsActive: ComputeIsActive(),
            Href: Href?.ToString() ?? string.Empty,
            NavAttrs: navAttrs);

        return Template(state);
    }

    private bool ComputeIsActive()
    {
        if (Href is null) return false;
        var route = LiveRenderContext.Current?.Services?.GetService<RouteState>();
        if (route is null) return false;
        return ActiveMatch switch
        {
            NavLinkMatch.Exact => MatchExact(route, Href.Value.Path, Href.Value.QueryString),
            NavLinkMatch.Prefix => MatchPrefix(route.Path, Href.Value.Path),
            _ => false
        };
    }

    private static bool MatchExact(RouteState route, string hrefPath, string? hrefQuery)
    {
        if (!PathsEqual(route.Path, hrefPath)) return false;
        if (string.IsNullOrEmpty(hrefQuery)) return true;

        foreach (var pair in ParseQuery(hrefQuery))
        {
            if (!route.Query.TryGetValue(pair.Key, out var values)) return false;
            if (!values.Contains(pair.Value)) return false;
        }

        return true;
    }

    private static bool MatchPrefix(string currentPath, string hrefPath)
    {
        var cur = TrimSlash(currentPath);
        var pre = TrimSlash(hrefPath);
        if (pre.Length == 0) return true;
        if (cur.Equals(pre, StringComparison.OrdinalIgnoreCase)) return true;
        return cur.Length > pre.Length
               && cur.StartsWith(pre, StringComparison.OrdinalIgnoreCase)
               && cur[pre.Length] == '/';
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(TrimSlash(a), TrimSlash(b), StringComparison.OrdinalIgnoreCase);

    private static string TrimSlash(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return s.Length > 1 && s[^1] == '/' ? s[..^1] : s;
    }

    private static IEnumerable<KeyValuePair<string, string>> ParseQuery(string qs)
    {
        var s = qs.StartsWith('?') ? qs[1..] : qs;
        if (s.Length == 0) yield break;

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

// Hands the user's Template the computed state and the data attributes that wire
// SPA navigation. Splat `NavAttrs` onto an <a> via `Data: state.NavAttrs`.
public readonly record struct NavLinkState(
    bool IsActive,
    string Href,
    IReadOnlyDictionary<string, string?> NavAttrs);
