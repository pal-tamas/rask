using Rask.Core.Routing;

namespace Rask.Example.Shop.Features.Home;

[Route("/")]
public sealed partial class HomePage : Component
{
    // BsBlock exposes only Id/Class (not Element's full HTML surface), so the width lives on a
    // plain Div wrapper rather than a Style: on the card.
    protected override Component? Render() =>
        Div(Class: "mx-auto my-5", Style: "max-width:540px")[
            BsCard(Class: "shadow-sm")[
                BsCardBody()[
                    BsCardTitle()["Hello, Rask! 👋"],
                    BsCardText(Class: "text-body-secondary")["Your app is ready. Scaffold the rest with the rask CLI:"],
                    Ul(Class: "mb-3")[
                        Li()[Code()["rask generate feature Product Name:string Price:decimal"], " — a full CRUD slice (entity, pages, tests)"],
                        Li()[Code()["rask generate page About"], " — a routed page"],
                        Li()[Code()["rask generate component Card"], " — a reusable component"],
                        Li()[Code()["rask dev"], " — run with hot reload"]
                    ],
                    P(Class: "mb-0 small text-body-secondary")[
                        "Edit this page in ",
                        Code()["HomePage.cs"],
                        " — drop a ",
                        Code()["HomePage.css"],
                        " beside it and its rules are scoped to this page. Full guides at ",
                        A(Href: "https://github.com/pal-tamas/rask")["the Rask docs"],
                        "."
                    ]
                ]
            ]
        ];
}
