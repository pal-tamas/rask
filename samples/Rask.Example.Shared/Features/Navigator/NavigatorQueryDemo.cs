using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

// The Navigator query-mutation widget promoted out of the former NavigatorPage so the Routing guide can
// host it live. Every button mutates the CURRENT URL's query through the scoped Navigator service and the
// component re-renders to reflect it — watch the address bar. Scoped to query mutation (SetQuery /
// RemoveQuery / ClearQuery) so it stays on the guide page rather than navigating away.
public sealed class NavigatorQueryDemo(Navigator nav, RouteState route) : Component
{
    protected override RenderResult Render() =>
        Div()[
            BsCard(Class: Bs.Join(Shadow.Sm, Border.None, Margin.Bottom(3)))[
                BsCardBody()[
                    Div(Class: "row g-3")[
                        Div(Class: "col-md-6")[
                            Span(Class: "text-secondary small text-uppercase")["Path"],
                            Div()[Code(Class: "fs-6", Id: "nav-path")[route.Path]]
                        ],
                        Div(Class: "col-md-6")[
                            Span(Class: "text-secondary small text-uppercase")["Query"],
                            Div()[
                                Code(Class: "fs-6", Id: "nav-query")[
                                    route.Query.Count == 0 ? "(empty)" : BuildQuery(route)
                                ]
                            ]
                        ]
                    ]
                ]
            ],
            Div(Class: "btn-group flex-wrap")[
                BsButton(Color: BsColor.Primary, Outline: true, Size: BsSize.Sm, Id: "nav-set-page1", OnClick: () => nav.SetQuery("page", "1"))[
                    "SetQuery page=1"],
                BsButton(Color: BsColor.Primary, Outline: true, Size: BsSize.Sm, Id: "nav-set-page2", OnClick: () => nav.SetQuery("page", "2"))[
                    "SetQuery page=2"],
                BsButton(Color: BsColor.Primary, Outline: true, Size: BsSize.Sm, Id: "nav-set-sort", OnClick: () => nav.SetQuery("sort", "asc"))[
                    "SetQuery sort=asc"],
                BsButton(Color: BsColor.Secondary, Outline: true, Size: BsSize.Sm, Id: "nav-remove-page", OnClick: () => nav.RemoveQuery("page"))[
                    "RemoveQuery page"],
                BsButton(Color: BsColor.Danger, Outline: true, Size: BsSize.Sm, Id: "nav-clear", OnClick: () => nav.ClearQuery())["ClearQuery"]
            ]
        ];

    private static string BuildQuery(RouteState route)
    {
        var parts = new List<string>();
        foreach (var kv in route.Query)
        {
            foreach (var v in kv.Value)
            {
                parts.Add($"{kv.Key}={v}");
            }
        }

        return string.Join("&", parts);
    }
}
