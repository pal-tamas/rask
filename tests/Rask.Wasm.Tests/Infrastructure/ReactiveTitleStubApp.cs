using Rask.Core;
using Rask.Core.Routing;
using static Rask.Core.Components.Generated;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Wasm.Tests.Infrastructure;

// A reactive <title> (and H1) bound to a counter that a click handler bumps — NO navigation.
// Exercises the non-navigation head-change path: the head delta must ride the diff as a
// fragment (previously a body-only diff froze the head), with no history.
internal sealed class ReactiveTitleStubApp : Component
{
    private int _count;

    protected override RenderResult Render() =>
        [
            Doctype(),
            Html()[
                Head()[Title()[$"count-{_count}"]],
                Body()[
                    H1()[$"count={_count}"],
                    Button(OnClick: () => _count++)["bump"]
                ]
            ]];
}
