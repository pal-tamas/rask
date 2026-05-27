using Rask.Core.Routing;

namespace Company.RaskWasm;

[Route("/")]
public sealed class HomePage : Component
{
    protected override RenderResult Render() =>
        Div(Class: "welcome-card")[
            H1(Class: "welcome-title")["Hello, Rask!"],
            P(Class: "welcome-lead")["Welcome to your new app."],
            P(Class: "welcome-hint")[
                "This card is styled by a sibling ",
                Code()["HomePage.css"],
                " file — selectors are auto-scoped to this component."
            ]
        ];
}
