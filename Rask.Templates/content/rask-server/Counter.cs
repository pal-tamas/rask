using Rask.Core.Routing;

namespace Company.RaskServer;

[Route("/counter")]
public sealed class Counter : Component
{
    private int _count;

    public override Component Render() =>
        Fragment(
            H1(Children: ["Counter"]),
            P(Children: [$"Current count: {_count}"]),
            Button(
                OnClick: () => _count++,
                Children: ["Click me"])
        );
}
