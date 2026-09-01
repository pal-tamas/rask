using Rask.Core.Routing;
using Rask.Example.Shared;

namespace Rask.Example.Wasm.Features;

// Written by WasmWatchAppFixture for the WASM hot-reload E2E and deleted afterwards.
// Not part of the sample — do not commit.
[Route("hot-reload-probe")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed partial class HotReloadProbePage : Component
{
    protected override Component? Render() =>
        H1.Id("probe")["hot-reload-probe-original"];
}
