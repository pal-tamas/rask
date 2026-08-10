using Rask.Core;
using static Rask.Core.Components.Generated;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Wasm.Tests.Infrastructure;

internal sealed partial class ThrowingStubApp : Component
{
    protected override Component? Head => Title["throw"];
    protected override string? HtmlLang => null;

    protected override Component? Render() =>
    [
        P["throwing app"],
        Button.OnClick(() => throw new InvalidOperationException("boom"))["go"]
    ];
}
