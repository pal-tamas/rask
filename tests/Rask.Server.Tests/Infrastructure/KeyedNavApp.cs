using Rask.Core;
using Rask.Core.Components;
using Rask.Core.Routing;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Server.Tests.Infrastructure;

// Reproduces the keyed-insert-during-navigation scenario: a layout-style app subscribes to
// RouteState.Changed and, when it navigates, inserts an item into a keyed list — so the keyed
// InsertSubtree rides the navigation diff (which on the server runs through the coalescing send
// loop). The rows are keyed via data-rask-key, and the list stays sorted so the navigation path
// chooses where the new row lands:
//   /add-head → 5  (before the [10,20,30] seed)
//   /add-mid  → 15 (between 10 and 20)
//   /add-tail → 40 (after 30)
// Each scenario exercises the same HTML-slice path; the regression is that the inserted row's
// fragment is sliced from post-head-splice HTML via frame offsets, which must be correct at any
// position. (#7's NOTE claimed this was broken; #14/#37's AdjustOffsetsFrom fixed it.)
public sealed partial class KeyedNavApp : Component
{
    private readonly List<int> _items = [10, 20, 30];
    private readonly RouteState _route;

    public KeyedNavApp(RouteState route) => _route = route;

    protected override void OnMount() => _route.Changed += OnRouteChanged;
    protected override void OnUnmount() => _route.Changed -= OnRouteChanged;

    private void OnRouteChanged()
    {
        var insert = _route.Path switch
        {
            "/add-head" => 5,
            "/add-mid" => 15,
            "/add-tail" => 40,
            _ => (int?)null
        };

        if (insert is { } value && !_items.Contains(value))
        {
            _items.Add(value);
            _items.Sort();
        }

        StateHasChanged();
    }

    protected override Component? Head => new Title()["keyed-nav"];
    protected override string? HtmlLang => null;

    protected override Component? Render() =>
    [
        new H1()[$"path={_route.Path} count={_items.Count}"],
        Ul()[
            _items.Select(i => Li(
                Class: "row",
                Data: new Dictionary<string, string?> { ["rask-key"] = i.ToString() })[
                $"item {i}"])
        ]
    ];
}
