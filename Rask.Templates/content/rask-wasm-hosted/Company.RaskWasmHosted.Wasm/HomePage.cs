using Rask.Core;
using Rask.Core.Routing;
using static Rask.Core.Tags;

namespace Company.RaskWasmHosted.Wasm;

[Route("/")]
public sealed class HomePage : Component
{
    public override Component Render() =>
        Fragment(
            H1(Children: ["Hello, Rask!"]),
            P(Children: ["Welcome to your new app."])
        );
}
