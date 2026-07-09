using Rask.Core.Routing;

namespace Company.RaskNative;

[Route("/counter")]
public sealed class Counter : Component
{
    private int _count;

    protected override Component? Render() =>
    [
        H1()["Counter"],
        P()[$"Current count: {_count}"],
        Button(OnClick: () => _count++)["Click me"]
    ];
}
