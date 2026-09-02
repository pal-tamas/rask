using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

// The Navigator query-mutation widget promoted out of the former NavigatorPage so the Routing guide can
// host it live. Every button mutates the CURRENT URL's query through the scoped Navigator service and the
// component re-renders to reflect it — watch the address bar. Scoped to query mutation (SetQuery /
// RemoveQuery / ClearQuery) so it stays on the guide page rather than navigating away.
public sealed partial class NavigatorQueryDemo(Navigator nav, RouteState route) : Component
{
    protected override Component? Render() =>
        Div[
            Div.Class($"{Tw.Card} shadow-sm border-0 mb-3")[
                Div.Class(Tw.CardBody)[
                    Div.Class("grid grid-cols-12 gap-4")[
                        Div.Class("md:col-span-6")[
                            Span.Class("text-slate-500 dark:text-slate-400 text-sm uppercase")["Path"],
                            Div[Code.Class("text-base").Id("nav-path")[route.Path]]
                        ],
                        Div.Class("md:col-span-6")[
                            Span.Class("text-slate-500 dark:text-slate-400 text-sm uppercase")["Query"],
                            Div[
                                Code.Class("text-base").Id("nav-query")[
                                    route.Query.Count == 0 ? "(empty)" : BuildQuery(route)
                                ]
                            ]
                        ]
                    ]
                ]
            ],
            Div.Class("flex-wrap")[
                Button.Type("button").Class(Tw.BtnOutlinePrimary)
                    .Id("nav-set-page1")
                    .OnClick(() => nav.SetQuery("page", "1"))[
                    "SetQuery page=1"],
                Button.Type("button").Class(Tw.BtnOutlinePrimary)
                    .Id("nav-set-page2")
                    .OnClick(() => nav.SetQuery("page", "2"))[
                    "SetQuery page=2"],
                Button.Type("button").Class(Tw.BtnOutlinePrimary)
                    .Id("nav-set-sort")
                    .OnClick(() => nav.SetQuery("sort", "asc"))[
                    "SetQuery sort=asc"],
                Button.Type("button").Class(Tw.BtnOutlineSecondary)
                    .Id("nav-remove-page")
                    .OnClick(() => nav.RemoveQuery("page"))[
                    "RemoveQuery page"],
                Button.Type("button").Class(Tw.BtnOutlineDanger)
                    .Id("nav-clear")
                    .OnClick(() => nav.ClearQuery())["ClearQuery"]
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
