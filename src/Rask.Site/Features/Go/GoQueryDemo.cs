using Rask.Core.Routing;

namespace Rask.Site.Features;

// The query widget the Routing guide hosts live. Every button changes the CURRENT URL's query with
// Go.With / Go.Without, and the component re-renders to reflect it — watch the address bar. Query changes
// only, so it stays on the guide page rather than navigating away.
public sealed partial class GoQueryDemo(RouteState route) : Component
{
    protected override Component? Render() =>
        Div[
            Ui.Card.Class("shadow-sm mb-3")[
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
                Ui.Button.Primary.Outline
                    .Id("nav-set-page1")
                    .OnClick(() => Go.With("page", "1"))["Go.With page=1"],
                Ui.Button.Primary.Outline
                    .Id("nav-set-page2")
                    .OnClick(() => Go.With("page", "2"))["Go.With page=2"],
                Ui.Button.Primary.Outline
                    .Id("nav-set-sort")
                    .OnClick(() => Go.With("sort", "asc"))["Go.With sort=asc"],
                Ui.Button.Outline
                    .Id("nav-remove-page")
                    .OnClick(() => Go.Without("page"))["Go.Without page"],
                Ui.Button.Error.Outline
                    .Id("nav-clear")
                    .OnClick(() => Go.Without())["Go.Without()"]
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
