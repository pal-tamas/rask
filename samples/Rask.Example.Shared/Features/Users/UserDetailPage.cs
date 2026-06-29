using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

[Route("users/{id}")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class UserDetailPage(Navigator nav) : Component
{
    [RouteParam] public string Id { get; set; } = string.Empty;
    [QueryParam("tab")] public string? Tab { get; set; }

    protected override RenderResult Head => Title()[$"User #{Id} — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            $"User #{Id}",
            "This page lives at /users/{id}. The Id property is bound from the URL segment and Tab from the ?tab= query string."),
        H2(Class: "h4 mt-4 mb-3")["Current binding"],
        BsCard(Class: Bs.Join(Margin.Bottom(3), Shadow.Sm, Border.None))[
            BsCardBody()[
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
            ["UserDetailPage.cs"],
            Notes:
            "Route templates use {name} for segments. Add a type constraint with {name:int}, optional with {name?}, or a catch-all with {**rest}. [RouteParam] without an argument matches by property name; pass a string to alias."),
        H2(Class: "h4 mt-5 mb-3")["Switch user"],
        Div(Class: "btn-group mb-3")[
            Button(
                Class: "btn btn-outline-primary btn-sm",
                OnClick: () => nav.NavigateTo("/users/1"))["User #1"],
            Button(
                Class: "btn btn-outline-primary btn-sm",
                OnClick: () => nav.NavigateTo("/users/42"))["#42"],
            Button(
                Class: "btn btn-outline-primary btn-sm",
                OnClick: () => nav.NavigateTo("/users/137"))["#137"]
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
