#pragma warning disable RASK014 // test-local components have no generated factories

using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Routing;

namespace Rask.Testing.Tests;

/// <summary>
///     Groups every class that installs the process-wide diagnostics sink, so no two of them run at the
///     same time.
/// </summary>
/// <remarks>
///     xUnit parallelises across CLASSES, and <c>CapturingDiagnostics.Install()</c> swaps a global. Two
///     classes overlapping means one test's captures land in the other's list and the first sees an empty
///     collection — which is what <c>CapturingDiagnostics_SeesAFaultTheFrameworkSwallowed</c> did under a
///     loaded full-suite run while passing every time on its own. Waiting longer cannot fix it: the
///     diagnostic was never going to arrive in that list.
/// </remarks>
[CollectionDefinition("rask-global-diagnostics", DisableParallelization = true)]
public sealed class GlobalDiagnosticsCollection;

/// <summary>
///     The four ergonomic gaps #610 listed alongside the structural one: targeting a handler by what it
///     says rather than where it sits, a download sink, route/query seeding, and a JS runtime that fails
///     loudly on a type mismatch.
/// </summary>
[Collection("rask-global-diagnostics")]
public class TestingSurfaceTests
{
    // ---- targeting a handler by what it is, not by its position ----

    private sealed class Toolbar : Component
    {
        public int Saves { get; private set; }
        public int Cancels { get; private set; }

        protected override Component? Render() =>
            Div[
                Button.Id("cancel").OnClick(() => Cancels++)["Cancel"],
                Button.Id("save").OnClick(() => Saves++)["Save"]
            ];
    }

    [Fact]
    public async Task ClickAsync_TargetsTheNamedElement_NotTheFirstHandlerInTheDocument()
    {
        var page = RaskTest.Render(new Toolbar());

        await page.On("#save").ClickAsync();

        // The point of the whole helper: "Save" is the SECOND click handler, so HandlerIds("click")[1]
        // would work today and silently re-point at something else the moment a button is added above it.
        Assert.Equal(1, page.Instance.Saves);
        Assert.Equal(0, page.Instance.Cancels);
    }

    [Fact]
    public void HandlerIdFor_SaysWhatTheElementIsActuallyWiredTo()
    {
        var page = RaskTest.Render(new Toolbar());

        var error = Assert.Throws<InvalidOperationException>(() => page.HandlerIdFor("#save", "input"));

        Assert.Contains("no input handler", error.Message, StringComparison.Ordinal);
        Assert.Contains("click", error.Message, StringComparison.Ordinal);
    }

    // ---- a download sink, so Navigator.Download stops throwing in a unit test ----

    private sealed class ExportPage(Navigator navigator) : Component
    {
        protected override Component? Render() =>
            Button
                .Id("export")
                .OnClick(() =>
                navigator.Download("orders.csv", "Id,Total\n1,9.99"u8.ToArray(), "text/csv"))["Export"];
    }

    [Fact]
    public async Task TestDownloadSink_RecordsWhatTheComponentStaged()
    {
        var downloads = new TestDownloadSink();
        var navigator = TestRoute.NavigatorFor(TestRoute.At("/orders"), downloads);
        var services = new ServiceCollection().AddSingleton(navigator).BuildServiceProvider();

        var page = RaskTest.Render(new ExportPage(navigator), services);
        await page.On("#export").ClickAsync();

        var file = Assert.Single(downloads.Staged);
        Assert.Equal("orders.csv", file.FileName);
        Assert.Equal("text/csv", file.ContentType);
        Assert.StartsWith("Id,Total", file.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void TestDownloadSink_HandsThemBackTheWayARealSinkDoes()
    {
        var sink = new TestDownloadSink();
        sink.Stage("a.txt", "one"u8.ToArray(), "text/plain");
        sink.Stage("b.txt", "two"u8.ToArray(), "text/plain");

        Assert.True(sink.TryConsume(out var first));
        Assert.Equal("a.txt", first!.Filename);
        Assert.True(sink.TryConsume(out _));
        Assert.False(sink.TryConsume(out _));

        // Consuming empties the queue but not the record — the assertion surface outlives the handoff.
        Assert.Equal(2, sink.Staged.Count);
    }

    // ---- route + query seeding ----

    [Fact]
    public void TestRoute_ParsesAndDecodesTheQueryString()
    {
        var state = TestRoute.At("/search?q=hello%20world&page=2");

        Assert.Equal("/search", state.Path);
        Assert.Equal("hello world", state.Query["q"].ToString());
        Assert.Equal("2", state.Query["page"].ToString());
    }

    [Fact]
    public void TestRoute_KeepsEveryValueOfARepeatedKey()
    {
        // What a multi-select or a checkbox group produces. Overwriting would make a page that reads all
        // of them look broken in a test and fine in a browser.
        var state = TestRoute.At("/filter?tag=a&tag=b");

        Assert.Equal(["a", "b"], state.Query["tag"].ToArray().Select(v => v ?? string.Empty));
    }

    [Fact]
    public void TestRoute_AddsTheLeadingSlash_SoBothSpellingsWork()
    {
        Assert.Equal("/orders", TestRoute.At("orders").Path);
    }

    // ---- a JS runtime that says when it dropped your value ----

    [Fact]
    public async Task TestJSRuntime_StillReturnsDefault_WhenNothingIsConfigured()
    {
        var js = new TestJSRuntime();

        Assert.Equal(0, await js.InvokeAsync<int>("getCount", args: null));
    }

    [Fact]
    public async Task TestJSRuntime_ThrowsWhenTheCannedValueIsTheWrongType()
    {
        // The trap #610 named: SetResponse stores the value boxed, and a boxed int is not a long — so this
        // used to return 0, indistinguishable from "not configured", and the test read as though the
        // component had ignored the value.
        var js = new TestJSRuntime();
        js.SetResponse("getCount", 1);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await js.InvokeAsync<long>("getCount", args: null));

        Assert.Contains("Int32", error.Message, StringComparison.Ordinal);
        Assert.Contains("Int64", error.Message, StringComparison.Ordinal);
    }

    // ---- diagnostics capture ----

    // A faulted async lifecycle hook: the framework's canonical swallow-and-log path. The continuation
    // runs off the dispatch's call stack, so there is nobody to throw to — it reports and carries on,
    // which is exactly the class of fault an app author had no supported way to assert on.
    private sealed class FaultsInMountAsync : Component
    {
        protected override async Task OnMountAsync()
        {
            await Task.Yield();
            throw new InvalidOperationException("boom");
        }

        protected override Component? Render() => Div["x"];
    }

    private static bool IsTheSwallowedFault(CapturedDiagnostic e) =>
        e.Level == DiagnosticLevel.Error && e.Exception?.Message == "boom";

    [Fact]
    public async Task CapturingDiagnostics_SeesAFaultTheFrameworkSwallowed()
    {
        using var diagnostics = CapturingDiagnostics.Install();

        _ = RaskTest.Render(new FaultsInMountAsync(), new ServiceCollection().BuildServiceProvider());

        // Swallow-and-log is the framework's designed behaviour here; without a capture there is no
        // supported way for an app author to assert that it happened, or that it didn't.
        await WaitForCaptureAsync(diagnostics, IsTheSwallowedFault);

        Assert.Contains(diagnostics.Captured, IsTheSwallowedFault);
    }

    [Fact]
    public async Task CapturingDiagnostics_RestoresThePreviousSinkOnDispose()
    {
        var before = CapturingDiagnostics.Install();
        before.Dispose();

        // Nothing observable to assert on directly — the sink is internal — so assert the property that
        // matters: a second install/dispose cycle still captures, which it wouldn't if the first had left
        // the global pointing at its own dead list.
        using var after = CapturingDiagnostics.Install();
        _ = RaskTest.Render(new FaultsInMountAsync(), new ServiceCollection().BuildServiceProvider());
        await WaitForCaptureAsync(after);

        Assert.NotEmpty(after.Captured);
    }

    // The hook faults on a thread-pool continuation, so the report lands after Render() returns.
    //
    // Waits for the diagnostic the caller is actually about, not merely for the list to become non-empty.
    // CapturingDiagnostics installs a PROCESS-GLOBAL sink, so a sibling test class running in parallel can
    // drop its own diagnostic in first; a "wait until non-empty" loop then returns before the awaited one
    // has landed and the assertion fails on a full-solution run while passing standalone.
    private static async Task WaitForCaptureAsync(
        CapturingDiagnostics diagnostics, Func<CapturedDiagnostic, bool>? match = null)
    {
        match ??= _ => true;

        for (var i = 0; i < 200 && !diagnostics.Captured.Any(match); i++)
        {
            await Task.Delay(10);
        }
    }
}
