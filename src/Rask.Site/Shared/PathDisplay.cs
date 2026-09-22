using Rask.Core.Routing;

namespace Rask.Site;

// Tiny route-aware component that shows the current path. Subscribes to
// RouteState.Changed so it re-renders on every nav (including browser back/forward),
// without forcing the surrounding layout to also be route-aware.
//
// It used to sit in the docs top bar and does not any more: the docs wear the landing page's bar now,
// which carries a wordmark, two links and the theme picker and no route readout. What it is FOR is
// unchanged — it is the worked example behind docs/routing.md's "Reacting to navigation" section, the
// smallest correct subscribe/unsubscribe pair in the repo, and PathDisplayTests holds it to that.
public sealed partial class PathDisplay(RouteState route) : Component
{
    protected override async Task Mount() => route.Changed += StateHasChanged;

    protected override async Task Unmount() => route.Changed -= StateHasChanged;

    protected override Component? Render() =>
        Span.Class("text-ui-muted text-sm hidden md:inline")[
            "path: ",
            Code.Class("text-info")[route.Path]
        ];
}
