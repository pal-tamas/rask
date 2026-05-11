using Rask.Core;
using Rask.Core.Components;
using static Rask.Core.Tags;

namespace Rask.Wasm.Tests.Infrastructure;

internal sealed class ThrowingStubApp : Component
{
    public override Component Render() =>
        Fragment(
            Doctype(),
            new Html(null,
                new Head(null, new Title(null, "throw")),
                new Body(null,
                    new P(null, "throwing app"),
                    Button(OnClick: () => throw new InvalidOperationException("boom"), Children: ["go"]))));
}
