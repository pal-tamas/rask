using Rask.Core;
using Rask.Core.Routing;

namespace Rask.Wasm.Tests.Infrastructure;

// An app that shows its page's title in BOTH places a real one does: the document <title>, written by the
// App itself above the router (where a scaffolded App has its HeadAssets), and a crumb in the layout the
// router renders. Both have rendered by the time the walk reaches the page that names itself.
internal sealed partial class PageTitleStubApp(RouteState route) : Component
{
    private static readonly IReadOnlyList<Route> _routes =
    [
        new Route(typeof(PageTitleStubLayout), "/wt",
        [
            new Route(typeof(PageTitleStubList), "list"),
            new Route(typeof(PageTitleStubEdit), "edit/{Id}"),
        ]),
    ];

    protected override Component? HeadAssets => Title[route.Title is { } t ? $"{t} | Stub" : "Stub"];

    protected override string? HtmlLang => null;

    protected override Component? Render() => Router.Routes(_routes);
}

internal sealed partial class PageTitleStubLayout(RouteState route) : Component
{
    protected override Component? Render() =>
        Div[Nav[route.Title is { } t ? Span.Id("crumb")[t] : null], Main[Outlet]];
}

internal sealed partial class PageTitleStubList : Component
{
    protected override string? PageTitle => "Records";

    protected override Component? Render() => H1["All records"];
}

// The record's number comes off the path rather than a [RouteParam]: these pages are routed by the App's own
// table and carry no [Route], which a bound parameter needs (RASK009).
internal sealed partial class PageTitleStubEdit(RouteState route) : Component
{
    protected override string? PageTitle => $"Edit Record {route.Path[(route.Path.LastIndexOf('/') + 1)..]}";

    protected override Component? Render() => H1[PageTitle];
}
