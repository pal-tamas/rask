using Rask.Core.Routing;

namespace Company.RaskWasm;

[Route("/")]
public sealed class HomePage : Component
{
    public override Component Render() =>
        Fragment()[
            H1()["Hello, Rask!"],
            P()["Welcome to your new app."]
        ];
}
