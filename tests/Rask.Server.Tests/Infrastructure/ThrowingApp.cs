using Rask.Core;
using Rask.Core.Components;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Server.Tests.Infrastructure;

public sealed partial class ThrowingApp : Component
{
    public int Counter;

    protected override Component? Render() =>
    [
        Doctype(),
        new Html()[new Head()[new Title()["throw"]],
            new Body()[new P()[$"count={Counter}"],
                Button(OnClick: () => throw new InvalidOperationException("boom"))["throw"],
                Button(OnClick: () => Counter++)["bump"]]]
    ];
}
