using Rask.Core;
using static Rask.Core.Components.Components;

namespace Rask.Wasm.Tests.Infrastructure;

internal sealed class ThrowingStubApp : Component
{
    protected override Component Render() =>
        Fragment(
            Doctype(),
            Html(Children:
            [
                Head(Children: [Title(Children: ["throw"])]),
                Body(Children:
                [
                    P(Children: ["throwing app"]),
                    Button(OnClick: () => throw new InvalidOperationException("boom"), Children: ["go"])
                ])
            ]));
}
