using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

[Route("navigator")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class NavigatorPage(Navigator nav, RouteState route) : Component
{
    protected override RenderResult Head => Title()["Navigator — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Navigator",
            "A scoped service that lets event-handler code change the route. It throws if you call it from Render() or during an initial GET — navigation belongs in event handlers."),
        H2(Class: "h4 mt-4 mb-3")["Current location"],
        BsCard(Class: Bs.Join(Shadow.Sm, Border.None, Margin.Bottom(4)))[
            BsCardBody()[
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
            BsButton(Color: BsColor.Primary, Outline: true, Size: BsSize.Sm, OnClick: () => nav.SetQuery("page", "1"))[
                "SetQuery page=1"],
            BsButton(Color: BsColor.Primary, Outline: true, Size: BsSize.Sm, OnClick: () => nav.SetQuery("page", "2"))[
                "SetQuery page=2"],
            BsButton(Color: BsColor.Primary, Outline: true, Size: BsSize.Sm, OnClick: () => nav.SetQuery("sort", "asc"))[
                "SetQuery sort=asc"],
            BsButton(Color: BsColor.Secondary, Outline: true, Size: BsSize.Sm, OnClick: () => nav.RemoveQuery("page"))[
                "RemoveQuery page"],
            BsButton(Color: BsColor.Danger, Outline: true, Size: BsSize.Sm, OnClick: () => nav.ClearQuery())["ClearQuery"]
        ],
        H2(Class: "h4 mt-4 mb-3")["Path navigation"],
        Div(Class: "d-flex flex-wrap gap-2 mb-4")[
            BsButton(Color: BsColor.Primary, Outline: true, Size: BsSize.Sm, OnClick: () => nav.NavigateTo("/navigator"))[BsIcon(Name: BsIconName.ArrowCounterclockwise, Class: "me-1"),
                "NavigateTo(\"/navigator\")"],
            BsButton(Color: BsColor.Primary, Outline: true, Size: BsSize.Sm, OnClick: () =>
                    nav.NavigateTo("/navigator", new[] { KeyValuePair.Create<string, string?>("from", "button") }))[
                BsIcon(Name: BsIconName.ArrowUpRight, Class: "me-1"), "NavigateTo(path, query)"]
        ],
        BsAlert(Color: BsColor.Info, Class: "d-flex align-items-start")[
            BsIcon(Name: BsIconName.InfoCircleFill, Class: "me-3 fs-4"),
            Div()[
                Strong()["Why event-handler only:"],
                " Navigator mutates RouteState and asks the dispatcher to push history. Doing that during Render() would mid-render the page out from under itself. Use it from button clicks, form submits, or lifecycle hooks that ran in response to an event."
            ]
        ],
        CodeSample(
            ["NavigatorDemo.cs"])
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
