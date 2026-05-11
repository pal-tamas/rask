using Rask.Core;
using Rask.Core.Routing;
using static Rask.Core.Tags;

namespace Rask.Example.Components;

[Route("/")]
public sealed class HomePage : Component
{
    public override Component Render() =>
        Fragment(
            H1(Children: ["Hello, world!"]),
            P(Class: "lead", Children: ["Welcome to your new app."])
        );
}
