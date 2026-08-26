using Microsoft.JSInterop;
using Rask.Core;
using Rask.Server.Tests.Infrastructure;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Server.Tests.Endpoints;

// OnMountAsync is fire-and-forget: the walk starts it, keeps walking, and the continuation paints
// later over the live connection. Right once a socket exists, wrong for the first response — where
// "later" is after the bytes have gone. So a page that loads its data in OnMountAsync served its
// placeholder as the first paint and as the whole document every crawler saw.
public class QuiescentRenderTests
{
    [Fact]
    public async Task Get_AwaitsOnMountAsync_AndServesTheData()
    {
        using var host = RaskTestHost.Create<AsyncDataApp>();

        var body = await host.Http.GetStringAsync("/");

        Assert.Contains("forecast-loaded", body);
        Assert.DoesNotContain("still-loading", body);
    }

    [Fact]
    public async Task Get_AwaitsWorkStartedByAResolvedWave()
    {
        // The second wave exists because resolved data mounts new components, which start their own
        // work. A single wait would serve the parent's data and the child's placeholder.
        using var host = RaskTestHost.Create<NestedAsyncDataApp>();

        var body = await host.Http.GetStringAsync("/");

        Assert.Contains("child-loaded", body);
        Assert.DoesNotContain("child-loading", body);
    }

    [Fact]
    public async Task Get_WhenWorkNeverSettles_StillAnswersWithinBudget()
    {
        using var host = RaskTestHost.Create<NeverSettlesApp>(
            configureServer: o => o.InitialRenderQuiescenceTimeout = TimeSpan.FromMilliseconds(150));

        var started = DateTime.UtcNow;
        var response = await host.Http.GetAsync("/");
        var elapsed = DateTime.UtcNow - started;

        response.EnsureSuccessStatusCode();
        // Served, not hung. The page keeps its live session and finishes loading over the socket
        // exactly as it does today.
        Assert.True(elapsed < TimeSpan.FromSeconds(5), $"took {elapsed}");
        Assert.Contains("still-loading", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Get_WhenTheHookAwaitsJavaScript_DoesNotBurnTheBudget()
    {
        // A JS call made during a render queues onto a frame, and during the GET there is no client
        // to send that frame to — so the awaiting task completes once the socket is up and never
        // before. Waiting for it buys nothing and costs the whole budget on EVERY page load. The
        // framework's own JWT auth sample restores its session exactly this way, which is how this
        // was found: every page of it took five seconds.
        using var host = RaskTestHost.Create<JsInteropOnMountApp>(
            configureServer: o => o.InitialRenderQuiescenceTimeout = TimeSpan.FromSeconds(5));

        var started = DateTime.UtcNow;
        var response = await host.Http.GetAsync("/");
        var elapsed = DateTime.UtcNow - started;

        response.EnsureSuccessStatusCode();
        Assert.True(elapsed < TimeSpan.FromSeconds(2), $"took {elapsed}; the budget was spent waiting");
    }

    [Fact]
    public async Task Get_WithTimeoutDisabled_KeepsTheSynchronousRender()
    {
        using var host = RaskTestHost.Create<AsyncDataApp>(
            configureServer: o => o.InitialRenderQuiescenceTimeout = TimeSpan.Zero);

        var body = await host.Http.GetStringAsync("/");

        // Zero is the documented opt-out, and it must genuinely opt out — not merely wait less.
        Assert.Contains("still-loading", body);
    }
}

public sealed partial class AsyncDataApp : Component
{
    private string? _forecast;

    protected override Component? HeadAssets => Title["async-data"];

    protected override async Task OnMountAsync()
    {
        await Task.Delay(20);
        _forecast = "forecast-loaded";
    }

    protected override Component? Render() => Div[_forecast ?? "still-loading"];
}

public sealed partial class NestedAsyncDataApp : Component
{
    private bool _ready;

    protected override Component? HeadAssets => Title["nested-async-data"];

    protected override async Task OnMountAsync()
    {
        await Task.Delay(20);
        _ready = true;
    }

    // The child only exists once the parent's data lands, so its own OnMountAsync cannot even
    // start until the second wave.
    protected override Component? Render() =>
        _ready ? Div[AsyncChild] : Div["parent-loading"];
}

public sealed partial class AsyncChild : Component
{
    private string? _value;

    protected override async Task OnMountAsync()
    {
        await Task.Delay(20);
        _value = "child-loaded";
    }

    protected override Component? Render() => Span[_value ?? "child-loading"];
}

public sealed partial class NeverSettlesApp : Component
{
    private string? _value;

    protected override Component? HeadAssets => Title["never-settles"];

    protected override async Task OnMountAsync()
    {
        await new TaskCompletionSource().Task;
        _value = "never";
    }

    protected override Component? Render() => Div[_value ?? "still-loading"];
}

public sealed partial class JsInteropOnMountApp(IJSRuntime js) : Component
{
    protected override Component? HeadAssets => Title["js-on-mount"];

    // Never completes during the GET: the call is queued for a frame that has no client yet.
    protected override async Task OnMountAsync() =>
        await js.InvokeAsync<string>("sessionStorage.getItem", "token");

    protected override Component? Render() => Div["content"];
}
