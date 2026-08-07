using Rask.Core;
using Rask.Core.Components;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Server.Tests.Infrastructure;

// App with a button whose click handler throws and a sibling button that increments a counter.
// Used to assert the WS dispatcher isolates a faulting handler: the throw is caught and logged,
// the dispatch lock is released, and the session stays usable so the next handler still renders.
public sealed partial class ThrowingHandlerApp : Component
{
    public int Counter;

    protected override Component? Render() =>
    [
        Doctype(),
        new Html()[new Head()[new Title()["throw"]],
            new Body()[
                new P()[$"count={Counter}"],
                Button(OnClick: () => throw new InvalidOperationException("boom in handler"))["boom"],
                Button(OnClick: () => Counter++)["bump"]]]
    ];
}
