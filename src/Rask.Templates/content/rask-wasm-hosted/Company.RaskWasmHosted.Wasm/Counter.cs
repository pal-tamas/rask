using Rask.Core.Routing;

namespace Company.RaskWasmHosted.Wasm;

[Route("/counter")]
public sealed class Counter : Component
{
    private int _count;

    protected override Component? Render() =>
        [
            H1()["Counter"],
            P()[$"Current count: {_count}"],
            BsButton(Color: BsColor.Primary,
                OnClick: () => _count++)["Click me"]
        ];
}
