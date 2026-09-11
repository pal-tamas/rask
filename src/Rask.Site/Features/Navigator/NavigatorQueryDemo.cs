using Rask.Core.Routing;

namespace Rask.Site.Features;

// The Navigator query-mutation widget promoted out of the former NavigatorPage so the Routing guide can
// host it live. Every button mutates the CURRENT URL's query through the scoped Navigator service and the
// component re-renders to reflect it — watch the address bar. Scoped to query mutation (SetQuery /
// RemoveQuery / ClearQuery) so it stays on the guide page rather than navigating away.
public sealed partial class NavigatorQueryDemo(Navigator nav, RouteState route) : Component
{
    protected override Component? Render() =>
        Div[
            UiCard.Class("shadow-sm mb-3")[
                    Div.Class("grid grid-cols-12 gap-4")[
                        Div.Class("col-span-12 md:col-span-6")[
                            Span.Class("text-ui-muted text-sm uppercase")["Path"],
                            Div[Code.Class("text-base").Id("nav-path")[route.Path]]
                        ],
                        Div.Class("col-span-12 md:col-span-6")[
                            Span.Class("text-ui-muted text-sm uppercase")["Query"],
                            Div[
                                Code.Class("text-base").Id("nav-query")[
                                    route.Query.Count == 0 ? "(empty)" : BuildQuery(route)
                                ]
                            ]
                        ]
                    ]
                ],
            Div.Class("flex-wrap")[
                UiButton.Tone(UiTone.Primary).Variant(UiVariant.Outline)
                    .Id("nav-set-page1")
                    .OnClick(() => nav.SetQuery("page", "1"))["SetQuery page=1"],
                UiButton.Tone(UiTone.Primary).Variant(UiVariant.Outline)
                    .Id("nav-set-page2")
                    .OnClick(() => nav.SetQuery("page", "2"))["SetQuery page=2"],
                UiButton.Tone(UiTone.Primary).Variant(UiVariant.Outline)
                    .Id("nav-set-sort")
                    .OnClick(() => nav.SetQuery("sort", "asc"))["SetQuery sort=asc"],
                UiButton.Variant(UiVariant.Outline)
                    .Id("nav-remove-page")
                    .OnClick(() => nav.RemoveQuery("page"))["RemoveQuery page"],
                UiButton.Tone(UiTone.Error).Variant(UiVariant.Outline)
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
