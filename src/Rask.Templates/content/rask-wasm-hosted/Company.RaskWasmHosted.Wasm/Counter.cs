using Rask.Core.Routing;

namespace Company.RaskWasmHosted.Wasm;

[Route("/counter")]
public sealed class Counter : Component
{
    private int _count;

    protected override RenderResult Render() =>
        [
            H1()["Counter"],
            P()[$"Current count: {_count}"],
            Button(
                OnClick: () => _count++)["Click me"]
        ];
}
