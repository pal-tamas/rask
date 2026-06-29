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
                        BsBadge(Color: BsColor.Primary, Class: "me-2")["RouteParam"],
                        Code()["Id"], " = ", Strong()[Id]
                    ],
                    Li()[
                        BsBadge(Color: BsColor.Secondary, Class: "me-2")["QueryParam"],
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
            BsButton(Color: BsColor.Primary, Outline: true, Size: BsSize.Sm, OnClick: () => nav.NavigateTo("/users/1"))["User #1"],
            BsButton(Color: BsColor.Primary, Outline: true, Size: BsSize.Sm, OnClick: () => nav.NavigateTo("/users/42"))["#42"],
            BsButton(Color: BsColor.Primary, Outline: true, Size: BsSize.Sm, OnClick: () => nav.NavigateTo("/users/137"))["#137"]
        ],
        Div()[
            BsButton(Color: BsColor.Primary, Size: BsSize.Sm, OnClick: () => nav.SetQuery("tab", Tab == "profile" ? "activity" : "profile"))[
                BsIcon(Name: BsIconName.ToggleOn, Class: "me-1"),
                "Toggle ?tab=", Tab == "profile" ? "activity" : "profile"
            ]
        ]
    ];
}
