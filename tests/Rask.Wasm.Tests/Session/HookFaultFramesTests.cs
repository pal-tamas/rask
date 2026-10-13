using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;
using Rask.Core.Routing;
using Rask.Wasm.Tests.Infrastructure;

namespace Rask.Wasm.Tests.Session;

// The browser host's side of a lifecycle hook that faults with no boundary of the app's around it: the frame
// that would have shown the page shows the error page a render fault shows, and a page already painted on
// its placeholder is repainted as it. Nothing of the exception is drawn.
[Collection("WasmSession")]
public sealed class HookFaultFramesTests() : ResettingTestBase(LiveDiffMode.DisabledFull)
{
    private const string Heading = "Something went wrong";

    private static readonly TimeSpan Patient = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Brief = TimeSpan.FromMilliseconds(40);

    [Theory]
    [InlineData("/mount")]
    [InlineData("/deep")]
    public async Task A_first_render_whose_hook_faults_before_its_first_await_is_the_error_page(string path)
    {
        HookFaultStubApp.Release();
        var (session, services) = NewSession<HookFaultStubApp>(diffMode: DiffMode, navigationBudget: Patient);
        services.GetRequiredService<RouteState>().Path = path;

        var first = Encoding.UTF8.GetString(await session.InitialRenderAsync());

        Assert.Contains(Heading, first, StringComparison.Ordinal);
        Assert.DoesNotContain("loading", first, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", first, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", first, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_first_render_whose_hook_faults_after_an_await_is_the_error_page()
    {
        HookFaultStubApp.Hold();
        var (session, services) = NewSession<HookFaultStubApp>(diffMode: DiffMode, navigationBudget: Patient);
        services.GetRequiredService<RouteState>().Path = "/mount";

        var rendering = session.InitialRenderAsync();
        await HookFaultStubApp.ReadStarted();
        HookFaultStubApp.Release();
        var first = Encoding.UTF8.GetString(await rendering);

        Assert.Contains(Heading, first, StringComparison.Ordinal);
        Assert.DoesNotContain("loading", first, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", first, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/mount", false)]
    [InlineData("/mount", true)]
    [InlineData("/deep", false)]
    [InlineData("/deep", true)]
    public async Task A_page_reached_by_a_link_whose_hook_faults_within_the_wait_arrives_as_the_error_page(string path, bool afterAnAwait)
    {
        HookFaultStubApp.Release();
        var (session, _) = NewSession<HookFaultStubApp>(diffMode: DiffMode, navigationBudget: Patient);
        await session.InitialRenderAsync();
        if (afterAnAwait)
        {
            HookFaultStubApp.Hold();
        }

        var navigating = session.DispatchAsync(Navigate(path));
        if (afterAnAwait)
        {
            await HookFaultStubApp.ReadStarted();
            HookFaultStubApp.Release();
        }

        var frame = Encoding.UTF8.GetString(await navigating);

        Assert.Contains(Heading, frame, StringComparison.Ordinal);
        Assert.Contains($"\"url\":\"{path}\"", frame, StringComparison.Ordinal);
        Assert.DoesNotContain("loading", frame, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", frame, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_page_painted_on_its_placeholder_whose_hook_then_faults_is_repainted_as_the_error_page()
    {
        HookFaultStubApp.Hold();
        var (session, _) = NewSession<HookFaultStubApp>(diffMode: DiffMode, navigationBudget: Brief);
        await session.InitialRenderAsync();

        var atTheBudget = Encoding.UTF8.GetString(await session.DispatchAsync(Navigate("/mount")));
        HookFaultStubApp.Release();
        await WaitFor.True(
            () => Encoding.UTF8.GetString(session.LastSentFrame).Contains(Heading, StringComparison.Ordinal),
            "the error page in a second frame");
        var after = Encoding.UTF8.GetString(session.LastSentFrame);

        Assert.Contains("loading", atTheBudget, StringComparison.Ordinal);
        Assert.DoesNotContain(Heading, atTheBudget, StringComparison.Ordinal);
        Assert.DoesNotContain("loading", after, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", after, StringComparison.Ordinal);
    }

    private static byte[] Navigate(string path) =>
        Encoding.UTF8.GetBytes($$"""{"type":"navigate","path":"{{path}}","query":""}""");
}
