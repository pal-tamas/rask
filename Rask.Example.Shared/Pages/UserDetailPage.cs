using Rask.Core.Routing;
using Rask.Example.Shared.Demos;
using Rask.Example.Shared.Layout;

namespace Rask.Example.Shared.Pages;

[Route("users/{id}")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class UserDetailPage(Navigator nav) : Component
{
    [RouteParam] public string Id { get; set; } = string.Empty;
    [QueryParam("tab")] public string? Tab { get; set; }

    protected override Component? Head => Title()[$"User #{Id} — Rask"];

    protected override Component Render() =>
        Fragment()[
            PageHeader.Render(
                $"User #{Id}",
                "This page lives at /users/{id}. The Id property is bound from the URL segment and Tab from the ?tab= query string."),
            H2(Class: "h4 mt-4 mb-3")["Current binding"],
            Div(Class: "card mb-3 shadow-sm border-0")[
                Div(Class: "card-body")[
                    Ul(Class: "list-unstyled mb-0")[
                        Li(Class: "mb-2")[
                            Span(Class: "badge text-bg-primary me-2")["RouteParam"],
                            Code()["Id"], " = ", Strong()[Id]
                        ],
                        Li()[
                            Span(Class: "badge text-bg-secondary me-2")["QueryParam"],
                            Code()["Tab"], " = ", Strong()[Tab ?? "(none)"]
                        ]
                    ]
                ]
            ],
            CodeSample(
                """
                [Route("users/{id}")]
                public sealed class UserDetailPage : Component
                {
                    [RouteParam] public string Id { get; set; } = string.Empty;
                    [QueryParam("tab")] public string? Tab { get; set; }

                    public override Component Render() => /* uses Id and Tab */;
                }
                """,
                Notes:
                "Route templates use {name} for segments. Add a type constraint with {name:int}, optional with {name?}, or a catch-all with {**rest}. [RouteParam] without an argument matches by property name; pass a string to alias."),
            H2(Class: "h4 mt-5 mb-3")["Switch user"],
            Div(Class: "btn-group mb-3")[
                Button(
                    Class: "btn btn-outline-primary btn-sm",
                    OnClick: () => nav.Navigate("/users/1"))["User #1"],
                Button(
                    Class: "btn btn-outline-primary btn-sm",
                    OnClick: () => nav.Navigate("/users/42"))["#42"],
                Button(
                    Class: "btn btn-outline-primary btn-sm",
                    OnClick: () => nav.Navigate("/users/137"))["#137"]
            ],
            Div()[
                Button(
                    Class: "btn btn-primary btn-sm",
                    OnClick: () => nav.SetQuery("tab", Tab == "profile" ? "activity" : "profile"))[
                        I(Class: "bi bi-toggle-on me-1"),
                        "Toggle ?tab=", Tab == "profile" ? "activity" : "profile"
                    ]
            ]
        ];
}
