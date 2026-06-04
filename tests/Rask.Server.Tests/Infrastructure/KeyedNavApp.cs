using Rask.Core;
using Rask.Core.Components;
using Rask.Core.Routing;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Server.Tests.Infrastructure;

// Reproduces the keyed-insert-during-navigation scenario: a layout-style app subscribes to
// RouteState.Changed and, when it navigates to "/add", appends an item to a keyed list — so
// the keyed InsertSubtree rides the navigation diff (which on the server runs through the
// coalescing send loop). The rows are keyed via data-rask-key.
public sealed class KeyedNavApp : Component
{
    private readonly RouteState _route;
    private readonly List<int> _items = [1, 2];

    public KeyedNavApp(RouteState route) => _route = route;

    protected override void OnMount() => _route.Changed += OnRouteChanged;
    protected override void OnUnmount() => _route.Changed -= OnRouteChanged;

    private void OnRouteChanged()
    {
        if (_route.Path == "/add" && !_items.Contains(3))
        {
            _items.Add(3);
        }

        StateHasChanged();
    }

    protected override RenderResult Render() =>
        [
            Doctype(),
            new Html()[
                new Head()[new Title()["keyed-nav"]],
                new Body()[
                    new H1()[$"path={_route.Path} count={_items.Count}"],
                    Ul()[
                        _items.Select(i => Li(
                            Class: "row",
                            Data: new Dictionary<string, string?> { ["rask-key"] = i.ToString() })[
                            $"item {i}"])
                    ]
                ]
            ]
        ];
}
