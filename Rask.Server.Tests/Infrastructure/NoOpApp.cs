using Rask.Core;
using Rask.Core.Components;
using static Rask.Core.Tags;

namespace Rask.Server.Tests.Infrastructure;

// Renders identically every render. The click handler mutates a field that the render tree
// does NOT observe, so repeated invocations produce byte-identical HTML payloads — used by
// the payload-dedup tests to verify that LiveSession suppresses redundant WS frames.
public sealed class NoOpApp : Component
{
    public int Hidden;

    protected override Component Render() =>
        Fragment(
            Doctype(),
            new Html { Children = [new Head { Children = [new Title { Children = ["noop"] }] },
                new Body { Children = [new H1 { Children = ["static"] },
                    Button(OnClick: () => Hidden++, Children: ["noop"])] }] });
}
