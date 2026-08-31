using Rask.Core.Routing;

namespace Rask.Example.Shared;

// Tiny route-aware component that shows the current path. Subscribes to
// RouteState.Changed so it re-renders on every nav (including browser back/forward),
// without forcing the surrounding layout to also be route-aware.
public sealed partial class PathDisplay(RouteState route) : Component
{
    protected override void OnMount() => route.Changed += StateHasChanged;

    protected override void OnUnmount() => route.Changed -= StateHasChanged;

    protected override Component? Render() =>
        Span.Class("text-slate-500 dark:text-slate-400 text-sm hidden md:inline")[
            "path: ",
            Code.Class("text-info")[route.Path]
        ];
}
