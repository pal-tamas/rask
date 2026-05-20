using Rask.Core.Routing;
using Rask.Example.Shared.Demos;
using Rask.Example.Shared.Layout;

namespace Rask.Example.Shared.Pages;

[Route("navigator")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class NavigatorPage(Navigator nav, RouteState route) : Component
{
    protected override Component? Head => Title()["Navigator — Rask"];

    protected override Component Render() =>
        Fragment()[
            PageHeader.Render(
                "Navigator",
                "A scoped service that lets event-handler code change the route. It throws if you call it from Render() or during an initial GET — navigation belongs in event handlers."),
            H2(Class: "h4 mt-4 mb-3")["Current location"],
            Div(Class: "card shadow-sm border-0 mb-4")[
                Div(Class: "card-body")[
                    Div(Class: "row g-3")[
                        Div(Class: "col-md-6")[
                            Span(Class: "text-secondary small text-uppercase")["Path"],
                            Div()[Code(Class: "fs-6")[route.Path]]
                        ],
                        Div(Class: "col-md-6")[
                            Span(Class: "text-secondary small text-uppercase")["Query"],
                            Div()[
                                Code(Class: "fs-6")[
                                    route.Query.Count == 0 ? "(empty)" : BuildQuery(route)
                                ]
                            ]
                        ]
                    ]
                ]
            ],
            H2(Class: "h4 mt-4 mb-3")["Query mutators"],
            P(Class: "text-secondary")[
                "Watch the URL bar — every button below mutates state and the page re-renders to reflect it."
            ],
            Div(Class: "btn-group flex-wrap mb-3")[
                Button(Class: "btn btn-outline-primary btn-sm", OnClick: () => nav.SetQuery("page", "1"))[
                    "SetQuery page=1"],
                Button(Class: "btn btn-outline-primary btn-sm", OnClick: () => nav.SetQuery("page", "2"))[
                    "SetQuery page=2"],
                Button(Class: "btn btn-outline-primary btn-sm", OnClick: () => nav.SetQuery("sort", "asc"))[
                    "SetQuery sort=asc"],
                Button(Class: "btn btn-outline-secondary btn-sm", OnClick: () => nav.RemoveQuery("page"))[
                    "RemoveQuery page"],
                Button(Class: "btn btn-outline-danger btn-sm", OnClick: () => nav.ClearQuery())["ClearQuery"]
            ],
            H2(Class: "h4 mt-4 mb-3")["Path navigation"],
            Div(Class: "d-flex flex-wrap gap-2 mb-4")[
                Button(
                    Class: "btn btn-outline-primary btn-sm",
                    OnClick: () => nav.Navigate("/navigator"))[I(Class: "bi bi-arrow-counterclockwise me-1"),
                    "Navigate(\"/navigator\")"],
                Button(
                    Class: "btn btn-outline-primary btn-sm",
                    OnClick: () =>
                        nav.Navigate("/navigator", new[] { KeyValuePair.Create<string, string?>("from", "button") }))[
                    I(Class: "bi bi-arrow-up-right me-1"), "Navigate(path, query)"]
            ],
            Div(Class: "alert alert-info d-flex align-items-start")[
                I(Class: "bi bi-info-circle-fill me-3 fs-4"),
                Div()[
                    Strong()["Why event-handler only:"],
                    " Navigator mutates RouteState and asks the dispatcher to push history. Doing that during Render() would mid-render the page out from under itself. Use it from button clicks, form submits, or lifecycle hooks that ran in response to an event."
                ]
            ],
            CodeSample(
                """
                Button(
                    OnClick: () => nav.Navigate("/dashboard"))["Open dashboard"]

                // Or update just the query, keeping the same path:
                Select(
                    OnChange: v => nav.SetQuery("sort", v))[...]
                """)
        ];

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
