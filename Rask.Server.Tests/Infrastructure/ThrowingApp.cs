using Rask.Core;
using Rask.Core.Components;

namespace Rask.Server.Tests.Infrastructure;

public sealed class ThrowingApp : Component
{
    public int Counter;

    protected override Component Render() =>
        Fragment(
            Doctype(),
            new Html { Children = [new Head { Children = [new Title { Children = ["throw"] }] },
                new Body { Children = [new P { Children = [$"count={Counter}"] },
                    Button(OnClick: () => throw new InvalidOperationException("boom"), Children: ["throw"]),
                    Button(OnClick: () => Counter++, Children: ["bump"])] }] });
}
