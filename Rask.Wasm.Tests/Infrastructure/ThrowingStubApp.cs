using Rask.Core;
using static Rask.Core.Components.Components;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Wasm.Tests.Infrastructure;

internal sealed class ThrowingStubApp : Component
{
    protected override RenderResult Render() =>
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
