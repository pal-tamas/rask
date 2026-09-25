using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Rask.Core.Forms;
using Rask.Core.Live;
using Rask.Core.ScopedAssets;
using Rask.Core.Tests.Interop;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.ScopedAssets;

// The runtime half of a component's typed calls into its scoped TypeScript — what the generated
// private methods call (see ScopedScriptCallsGenerator).
public class ScopedScriptTests
{
    [Fact]
    public async Task A_call_goes_through_the_session_runtime_under_the_exports_identifier()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("Rask.Probe.width", 42.0);
        var probe = new Probe();
        using var scope = DispatchServicesScope.Push(Services(js));

        var width = await ScopedScript.Call<double>(probe, "Rask.Probe.width", ["box"]);

        Assert.Equal(42.0, width);
        Assert.Equal(["box"], js.ArgsFor("Rask.Probe.width"));
    }

    [Fact]
    public async Task A_call_from_a_component_not_on_a_page_says_so()
    {
        var probe = new Probe();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await ScopedScript.Call(probe, "Rask.Probe.mount", []));

        Assert.Contains("Probe called its scoped TypeScript before it was on a page", error.Message);
    }

    [Fact]
    public void A_callback_travels_as_a_marker_the_browser_revives()
    {
        var probe = new Probe();

        var marker = ScopedScript.Callback(probe, new Callback(() => { }));

        var json = JsonSerializer.Serialize(marker, marker.GetType(), ScopedScriptJsonContext.Default.Options);
        Assert.Matches("""^\{"__raskCb__":\d+\}$""", json);
    }

    [Fact]
    public async Task A_callback_the_script_calls_runs_with_its_argument_and_repaints_the_component()
    {
        var probe = new Probe();
        double? seen = null;
        var marker = (ScopedScript.ScriptCallback)ScopedScript.Callback(probe, new Callback<double>(n => seen = n));

        await ScopedScript.Invoke(marker.Id, JsonDocument.Parse("[3.5]").RootElement);

        Assert.Equal(3.5, seen);
        Assert.True(probe.IsRenderRequestedForTest);
    }

    [Fact]
    public async Task A_callback_answers_only_the_session_that_handed_it_over()
    {
        // RaskScopedCallback is callable from any socket, and on the Server host the registry is shared by every
        // session — so another session posting this id must not reach the callback.
        var mine = new FakeJsRuntime();
        var probe = new Probe();
        var calls = 0;
        ScopedScript.ScriptCallback marker;
        using (DispatchServicesScope.Push(Services(mine)))
        {
            marker = (ScopedScript.ScriptCallback)ScopedScript.Callback(probe, new Callback(() => calls++));
        }

        using (JsCaller.Enter(new FakeJsRuntime()))
        {
            await ScopedScript.Invoke(marker.Id, JsonDocument.Parse("[]").RootElement);
        }

        var fromAnotherSession = calls;
        using (JsCaller.Enter(mine))
        {
            await ScopedScript.Invoke(marker.Id, JsonDocument.Parse("[]").RootElement);
        }

        Assert.Equal(0, fromAnotherSession);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Trailing_unset_optional_arguments_are_left_out_so_the_script_sees_undefined()
    {
        object?[] args = ["name", null, null];

        var trimmed = ScopedScript.Trim(args, 1);

        Assert.Equal(["name"], trimmed);
        Assert.Equal(["a", null, "c"], ScopedScript.Trim(["a", null, "c"], 1));
        Assert.Equal([null], ScopedScript.Trim([null], 1));
    }

    [Fact]
    public async Task A_call_made_from_OnUnmount_still_reaches_the_script()
    {
        var js = new FakeJsRuntime();
        var probe = new Probe();
        using var scope = DispatchServicesScope.Push(Services(js));

        ComponentLifecycle.DisposeComponentTree(probe);
        await ScopedScript.Call(probe, "Rask.Probe.stop", []);

        Assert.Equal(1, js.CallCount("Rask.Probe.stop"));
    }

    [Fact]
    public async Task A_callback_handed_over_after_unmount_is_never_kept()
    {
        var probe = new Probe();
        var calls = 0;
        ComponentLifecycle.DisposeComponentTree(probe);
        var before = ScopedScript.CallbackCount;

        var marker = (ScopedScript.ScriptCallback)ScopedScript.Callback(probe, new Callback(() => calls++));
        await ScopedScript.Invoke(marker.Id, JsonDocument.Parse("[]").RootElement);

        Assert.Equal(0, calls);
        Assert.True(ScopedScript.CallbackCount <= before);
    }

    [Fact]
    public async Task A_script_object_created_after_unmount_is_released_at_once()
    {
        var probe = new Probe();
        var reference = new FakeObjectReference();
        ComponentLifecycle.DisposeComponentTree(probe);

        ScopedScript.Adopt(probe, new Chart(reference));
        await Task.Yield();

        Assert.Equal(1, reference.DisposeCount);
    }

    [Fact]
    public async Task A_callback_is_released_when_its_component_unmounts()
    {
        var probe = new Probe();
        var calls = 0;
        var marker = (ScopedScript.ScriptCallback)ScopedScript.Callback(probe, new Callback(() => calls++));

        ComponentLifecycle.DisposeComponentTree(probe);
        await ScopedScript.Invoke(marker.Id, JsonDocument.Parse("[]").RootElement);

        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task A_script_object_is_disposed_when_its_component_unmounts()
    {
        var probe = new Probe();
        var reference = new FakeObjectReference();
        ScopedScript.Adopt(probe, new Chart(reference));

        ComponentLifecycle.DisposeComponentTree(probe);
        await Task.Yield();

        Assert.Equal(1, reference.DisposeCount);
    }

    [Fact]
    public async Task Disposing_a_script_object_early_releases_it_once()
    {
        var probe = new Probe();
        var reference = new FakeObjectReference();
        var chart = ScopedScript.Adopt(probe, new Chart(reference));

        await chart.DisposeAsync();
        ComponentLifecycle.DisposeComponentTree(probe);

        Assert.Equal(1, reference.DisposeCount);
    }

    private static IServiceProvider Services(IJSRuntime js) =>
        new ServiceCollection().AddSingleton(js).BuildServiceProvider();

    private sealed class Probe : Component
    {
        protected override Component? Render() => null;
    }

    private sealed class Chart(IJSObjectReference reference) : ScriptObject(reference);

    private sealed class FakeObjectReference : IJSObjectReference
    {
        public int DisposeCount { get; private set; }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => default;

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            default;
    }
}
