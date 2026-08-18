using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Live;
using Rask.Core.Routing;

namespace Rask.Chrome.Components;

/// <summary>
///     Primary navigation across the bottom of a screen. One declaration serves every host: the web hosts
///     render a navigation landmark of real links, and a native shell projects it to a <c>UITabBar</c> (iOS) /
///     bottom navigation (Android), emitting no HTML at all.
/// </summary>
/// <example>
///     <code>
/// protected override Component? TabBar =>
///     TabStrip.Tabs([
///         TabItem.Title("Home").Icon(BarIcon.Home).To(Features.Routes.Home()),
///         TabItem.Title("Me").Icon(BarIcon.Person).To(Features.Routes.Me()).Badge("3"),
///     ]);
///     </code>
/// </example>
/// <remarks>
///     <para>
///         <b>Which tab is active.</b> Leave <see cref="Selected" /> unset and the framework derives it from
///         the current route, so the highlighted tab tracks navigation — a tap, hardware Back, or a deep link —
///         without the caller re-deriving it. Set it to pin a tab. The derivation is
///         <see cref="DeriveSelected" />, and the native host calls the same method, so web and native cannot
///         disagree about which tab is lit.
///     </para>
///     <para>
///         <b>Web markup.</b> A <c>div</c> with <c>role="navigation"</c> — the landmark a <c>&lt;nav&gt;</c>
///         maps to — carrying <c>.rask-tab-bar</c>, one <c>a.rask-tab</c> per tab, the active one marked
///         <c>.rask-tab-active</c> and <c>aria-current="page"</c>, and a badge in <c>.rask-tab-badge</c>. Core
///         ships no stylesheet: the class names are the styling contract.
///     </para>
///     <para>
///         For platform-exact native tab bars (per-bar tinting, an unselected tint) reach for
///         <c>Rask.Native</c>'s <c>NativeTabBar</c> instead — the native-only escape hatch. This type is the
///         portable subset.
///     </para>
/// </remarks>
public sealed partial class TabStrip : Component
{
    private RouteState? _route;

    /// <summary>The tabs, in order.</summary>
    public IReadOnlyList<TabItem>? Tabs { get; set; }

    /// <summary>
    ///     The selected tab index. Leave <c>null</c> (the default) to derive it from the current route; set it
    ///     to pin a specific tab.
    /// </summary>
    public int? Selected { get; set; }

    /// <summary>
    ///     The index of the tab matching <paramref name="currentPath" />: the tab whose path is the longest
    ///     prefix of it, so a nested route (<c>/todos/42</c>) keeps its section's tab (<c>/todos</c>) lit.
    ///     Returns <c>0</c> when nothing matches, because a tab bar with no lit tab reads as broken rather
    ///     than as "you are somewhere else".
    /// </summary>
    /// <remarks>
    ///     Shared with the native host's descriptor builder, so both ends of the same declaration light the
    ///     same tab. A trailing-segment check ("/" or end of string) keeps <c>/todos</c> from claiming
    ///     <c>/todos-archive</c>.
    /// </remarks>
    internal static int DeriveSelected(IReadOnlyList<TabItem>? tabs, string? currentPath)
    {
        if (tabs is not { Count: > 0 })
        {
            return 0;
        }

        var path = string.IsNullOrEmpty(currentPath) ? "/" : currentPath;
        var best = 0;
        var bestLength = -1;
        for (var i = 0; i < tabs.Count; i++)
        {
            var tabPath = tabs[i].To.Path;
            if (string.IsNullOrEmpty(tabPath) || !path.StartsWith(tabPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // "/todos" must not claim "/todos-archive": the character after the match has to end the segment.
            if (path.Length > tabPath.Length && tabPath[^1] != '/' && path[tabPath.Length] != '/')
            {
                continue;
            }

            if (tabPath.Length > bestLength)
            {
                best = i;
                bestLength = tabPath.Length;
            }
        }

        return best;
    }

    protected override void OnMount()
    {
        // Re-render when the route changes so the lit tab follows navigation. Same pattern as
        // DefaultNotFoundPage: subscribe at mount rather than reading route state as ambient render input.
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

    protected override Component? Render()
    {
        // On a native head the bar is a real platform widget the host builds from these properties.
        if (IsNative || Tabs is not { Count: > 0 } tabs)
        {
            return null;
        }

        var route = LiveRenderContext.Current?.Services?.GetService<RouteState>();
        var selected = Selected ?? DeriveSelected(tabs, route?.Path);

        var links = new List<Component>(tabs.Count);
        for (var i = 0; i < tabs.Count; i++)
        {
            var tab = tabs[i];
            var active = i == selected;
            var link = A
                .Href(tab.To.ToString())
                .Class(active ? "rask-tab rask-tab-active" : "rask-tab")
                .Data(new Dictionary<string, string?> { ["rask-icon"] = tab.Icon.Name });
            // aria-current="page" is what tells a screen reader which tab you are on. The active class alone
            // is a visual cue only.
            if (active)
            {
                link = link.Aria(new Dictionary<string, string?> { ["current"] = "page" });
            }

            links.Add(tab.Badge is { Length: > 0 } badge
                ? link[tab.Title, Div.Class("rask-tab-badge")[badge]]
                : link[tab.Title]);
        }

        return Div.Class("rask-tab-bar").Role("navigation")[links];
    }
}
