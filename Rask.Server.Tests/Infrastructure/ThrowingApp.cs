using Rask.Core;
using Rask.Core.Components;
using static Rask.Core.Tags;

namespace Rask.Server.Tests.Infrastructure;

public sealed class ThrowingApp : Component
{
    public int Counter;

    protected override Component Render() =>
        Fragment(
            Doctype(),
            new Html(null,
                new Head(null, new Title(null, "throw")),
                new Body(null,
                    new P(null, $"count={Counter}"),
                    Button(OnClick: () => throw new InvalidOperationException("boom"), Children: ["throw"]),
                    Button(OnClick: () => Counter++, Children: ["bump"]))));
}
