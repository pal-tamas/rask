using System.Text;
using Rask.Wasm.Tests.Infrastructure;

namespace Rask.Wasm.Tests.Session;

// Regression: InitialRenderAsync set InHandlerScope=true and then called BuildPayloadAsync
// DIRECTLY rather than through BuildPayloadCoalescingRerendersAsync. Any StateHasChanged raised
// while that first payload was being built — canonically an OnMountAsync continuation resolving
// mid-render — took the InHandlerScope short-circuit, set _pendingRenderInScope, and was then
// dropped on the floor because nothing on the initial-render path ever drains that flag. The page
// kept its first-paint markup (a spinner) until some unrelated event forced another dispatch.
//
// WASM-only by construction: the Server host has no in-session initial render at all — its first
// paint is the HTTP response, and every WS render already goes through the coalescing path.
[Collection("WasmSession")]
public class InitialRenderCoalesceTests : ResettingTestBase
{
    [Fact]
    public async Task InitialRender_StateChangedDuringFirstBuild_LandsInTheFirstFrame()
    {
        var (session, _) = NewSession<InitialRenderStateChangeApp>();

        var frame = await session.InitialRenderAsync();
        var html = Encoding.UTF8.GetString(frame);

        Assert.Contains("loaded", html, StringComparison.Ordinal);
        Assert.DoesNotContain("pending", html, StringComparison.Ordinal);
    }
}
