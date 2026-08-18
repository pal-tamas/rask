using Rask.Core.Routing;

namespace Rask.Example.Shop.Features.Home;

[Route("/")]
public sealed partial class HomePage : Component
{
    // BsBlock exposes only Id/Class (not Element's full HTML surface), so the width lives on a
    // plain Div wrapper rather than a .Style() on the card.
    protected override Component? Render() =>
        Div.Class("mx-auto my-5").Style("max-width:540px")[
            BsCard.Class("shadow-sm")[
                BsCardBody[
                    BsCardTitle["Hello, Rask! 👋"],
                    BsCardText.Class("text-body-secondary")["Your app is ready. What to do next:"],
                    Ul.Class("mb-3")[
                        Li[Code["rask dev"], " — run with hot reload"],
                        Li[Code["rask db add Init"], " then ", Code["rask db update"], " — create the database"],
                        Li[A.Href("https://github.com/pal-tamas/rask/blob/main/docs/tutorial/02-first-feature.md")["Build your first feature"], " — entity, pages and CQRS handlers, step by step"]
                    ],
                    P.Class("mb-0 small text-body-secondary")[
                        "Edit this page in ",
                        Code["HomePage.cs"],
                        " — drop a ",
                        Code["HomePage.css"],
                        " beside it and its rules are scoped to this page. Full guides at ",
                        A.Href("https://github.com/pal-tamas/rask")["the Rask docs"],
                        "."
                    ]
                ]
            ]
        ];
}
