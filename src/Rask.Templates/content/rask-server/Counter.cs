using Rask.Core.Routing;

namespace Company.RaskServer;

[Route("/counter")]
public sealed class Counter : Component
{
    private int _count;

    protected override RenderResult Render() =>
        [
            H1()["Counter"],
            P()[$"Current count: {_count}"],
            BsButton(Color: BsColor.Primary,
                OnClick: () => _count++)["Click me"]
        ];
}
