using Rask.Core.Routing;

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
