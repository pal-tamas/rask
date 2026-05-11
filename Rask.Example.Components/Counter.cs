using Rask.Core;
using Rask.Core.Routing;
using static Rask.Core.Tags;

namespace Rask.Example.Components;

[Route("/counter")]
public sealed class Counter : Component
{
    private int _count;

    public override Component Render() =>
        Fragment(
            H1(Children: ["Counter"]),
            P(Class: "fs-5", Children: [$"Current count: {_count}"]),
            Button(
                Class: "btn btn-primary",
                OnClick: () => _count++,
                Children: ["Click me"])
        );
}
