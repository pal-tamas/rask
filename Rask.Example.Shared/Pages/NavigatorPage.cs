using Rask.Core.Routing;

namespace Rask.Example.Shared;

[Route("navigator")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class NavigatorPage(Navigator nav, RouteState route) : Component
{
    protected override Component Render() =>
        Fragment(
            PageHeader.Render(
                "Navigator",
                "A scoped service that lets event-handler code change the route. It throws if you call it from Render() or during an initial GET — navigation belongs in event handlers."),
            H2(Class: "h4 mt-4 mb-3", Children: ["Current location"]),
            Div(Class: "card shadow-sm border-0 mb-4", Children:
            [
                Div(Class: "card-body", Children:
                [
                    Div(Class: "row g-3", Children:
                    [
                        Div(Class: "col-md-6", Children:
                        [
                            Span(Class: "text-secondary small text-uppercase", Children: ["Path"]),
                            Div(Children: [Code(Class: "fs-6", Children: [route.Path])])
                        ]),
                        Div(Class: "col-md-6", Children:
                        [
                            Span(Class: "text-secondary small text-uppercase", Children: ["Query"]),
                            Div(Children:
                            [
                                Code(Class: "fs-6", Children:
                                [
                                    route.Query.Count == 0 ? "(empty)" : BuildQuery(route)
                                ])
                            ])
                        ])
                    ])
                ])
            ]),
            H2(Class: "h4 mt-4 mb-3", Children: ["Query mutators"]),
            P(Class: "text-secondary", Children:
            [
                "Watch the URL bar — every button below mutates state and the page re-renders to reflect it."
            ]),
            Div(Class: "btn-group flex-wrap mb-3", Children:
            [
                Button(Class: "btn btn-outline-primary btn-sm", OnClick: () => nav.SetQuery("page", "1"),
                    Children: ["SetQuery page=1"]),
                Button(Class: "btn btn-outline-primary btn-sm", OnClick: () => nav.SetQuery("page", "2"),
                    Children: ["SetQuery page=2"]),
                Button(Class: "btn btn-outline-primary btn-sm", OnClick: () => nav.SetQuery("sort", "asc"),
                    Children: ["SetQuery sort=asc"]),
                Button(Class: "btn btn-outline-secondary btn-sm", OnClick: () => nav.RemoveQuery("page"),
                    Children: ["RemoveQuery page"]),
                Button(Class: "btn btn-outline-danger btn-sm", OnClick: () => nav.ClearQuery(),
                    Children: ["ClearQuery"])
            ]),
            H2(Class: "h4 mt-4 mb-3", Children: ["Path navigation"]),
            Div(Class: "d-flex flex-wrap gap-2 mb-4", Children:
            [
                Button(
                    Class: "btn btn-outline-primary btn-sm",
                    OnClick: () => nav.Navigate("/navigator"),
                    Children: [I(Class: "bi bi-arrow-counterclockwise me-1"), "Navigate(\"/navigator\")"]),
                Button(
                    Class: "btn btn-outline-primary btn-sm",
                    OnClick: () =>
                        nav.Navigate("/navigator", new[] { KeyValuePair.Create<string, string?>("from", "button") }),
                    Children: [I(Class: "bi bi-arrow-up-right me-1"), "Navigate(path, query)"])
            ]),
            Div(Class: "alert alert-info d-flex align-items-start", Children:
            [
                I(Class: "bi bi-info-circle-fill me-3 fs-4"),
                Div(Children:
                [
                    Strong(Children: ["Why event-handler only:"]),
                    " Navigator mutates RouteState and asks the dispatcher to push history. Doing that during Render() would mid-render the page out from under itself. Use it from button clicks, form submits, or lifecycle hooks that ran in response to an event."
                ])
            ]),
            Components.CodeSample(
                """
                Button(
                    OnClick: () => nav.Navigate("/dashboard"),
                    Children: ["Open dashboard"])

                // Or update just the query, keeping the same path:
                Select(
                    OnChange: v => nav.SetQuery("sort", v),
                    Children: [...])
                """)
        );

    private static string BuildQuery(RouteState route)
    {
        var parts = new List<string>();
        foreach (var kv in route.Query)
        foreach (var v in kv.Value)
        {
            parts.Add($"{kv.Key}={v}");
        }

        return string.Join("&", parts);
    }
}
