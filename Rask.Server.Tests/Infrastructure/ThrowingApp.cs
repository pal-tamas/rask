using Rask.Core;
using Rask.Core.Components;

namespace Rask.Server.Tests.Infrastructure;

public sealed class ThrowingApp : Component
{
    public int Counter;

    protected override Component Render() =>
        Fragment()[
            Doctype(),
            new Html()[new Head()[new Title()["throw"]],
                new Body()[new P()[$"count={Counter}"],
                    Button(OnClick: () => throw new InvalidOperationException("boom"))["throw"],
                    Button(OnClick: () => Counter++)["bump"]]]];
}
