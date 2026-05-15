using Rask.Core.Routing;

namespace Company.RaskServer;

[Route("/")]
public sealed class HomePage : Component
{
    protected override Component Render() =>
        Fragment()[
            H1()["Hello, Rask!"],
            P()["Welcome to your new app."]
        ];
}
