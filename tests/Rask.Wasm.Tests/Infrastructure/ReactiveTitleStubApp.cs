using Rask.Core;
using static Rask.Core.Components.Generated;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Wasm.Tests.Infrastructure;

// A reactive <title> (and H1) bound to a counter that a click handler bumps — NO navigation.
// Exercises the non-navigation head-change path: the head delta must ride the diff as a
// fragment (previously a body-only diff froze the head), with no history.
internal sealed partial class ReactiveTitleStubApp : Component
{
    private int _count;

    protected override Component? HeadAssets => Title[$"count-{_count}"];
    protected override string? HtmlLang => null;

    protected override Component? Render() =>
    [
        H1[$"count={_count}"],
        Button.OnClick(() => _count++)["bump"]
    ];
}
