using Rask.Core;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Wasm.Tests.Infrastructure;

/// <summary>
///     An <c>OnMountAsync</c> that suspends and then sets state while the session's INITIAL render is
///     still in flight — the shape a first-paint data fetch has on WASM.
/// </summary>
/// <remarks>
///     The two gates make the race deterministic instead of one-in-six. The mount continuation is held
///     until <c>OnRendered</c>, which runs after the page HTML has already been materialised, so the
///     value it sets provably cannot reach the frame that was just built. The render pass is then held
///     until the continuation has called <see cref="Component.StateHasChanged" />, so the repaint
///     request provably arrives while the initial build is still in scope. Only another build can
///     deliver it.
/// </remarks>
internal sealed partial class InitialRenderStateChangeApp : Component, IDisposable
{
    private readonly ManualResetEventSlim _painted = new(false);
    private readonly ManualResetEventSlim _requested = new(false);
    private string _value = "pending";

    public void Dispose()
    {
        _painted.Dispose();
        _requested.Dispose();
    }

    protected override Component? HeadAssets => Title["initial-state"];
    protected override string? HtmlLang => null;

    protected override async Task OnMountAsync()
    {
        // Suspend so the hook does NOT take InvokeAsyncLifecycleWithRendering's synchronous fast
        // path: this models a fetch, not an already-cached value.
        await Task.Yield();

        // Hold until the first paint's HTML exists, so this mutation is unambiguously too late for it.
        _painted.Wait(TimeSpan.FromSeconds(10));

        _value = "loaded";
        StateHasChanged();
        _requested.Set();
    }

    protected override void OnRendered(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        _painted.Set();
        _requested.Wait(TimeSpan.FromSeconds(10));
    }

    protected override Component? Render() => Div[_value];
}
