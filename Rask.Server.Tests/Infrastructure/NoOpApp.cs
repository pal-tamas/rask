using Rask.Core;
using Rask.Core.Components;

namespace Rask.Server.Tests.Infrastructure;

// Renders identically every render. The click handler mutates a field that the render tree
// does NOT observe, so repeated invocations produce byte-identical HTML payloads — used by
// the payload-dedup tests to verify that LiveSession suppresses redundant WS frames.
public sealed class NoOpApp : Component
{
    public int Hidden;

    protected override Component Render() =>
        Fragment()[
            Doctype(),
            new Html()[new Head()[new Title()["noop"]],
                new Body()[new H1()["static"],
                    Button(OnClick: () => Hidden++)["noop"]]]];
}
