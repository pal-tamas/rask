using Rask.Core;
using static Rask.Core.Components.Components;

namespace Rask.Wasm.Tests.Infrastructure;

internal sealed class ThrowingStubApp : Component
{
    protected override Component Render() =>
        Fragment()[
            Doctype(),
            Html()[
                Head()[Title()["throw"]],
                Body()[
                    P()["throwing app"],
                    Button(OnClick: () => throw new InvalidOperationException("boom"))["go"]
                ]
            ]];
}
