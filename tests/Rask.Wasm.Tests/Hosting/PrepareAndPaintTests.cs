using Rask.Core;
namespace Rask.Wasm.Tests.Hosting;

// A takeover needs an app that is ready to render and does NOT render: arriving into a page another
// runtime is still driving, painting on arrival would put two runtimes into one document. PrepareAsync
// stops at that line and PaintAsync crosses it.
//
// Only the pairing guard is reachable here. Preparing for real imports the JS bridge and boots the
// .NET runtime, so "prepared but unpainted" is a browser fact and belongs in an E2E test once there is
// a takeover to drive it.
public sealed class PrepareAndPaintTests
{
    [Fact]
    public async Task PaintingWithoutPreparing_SaysWhatIsMissing()
    {
        var builder = WasmHostBuilder.CreateDefault();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => builder.PaintAsync());

        // The failure this replaces is a silent no-op: a takeover that hands over to a runtime which
        // never prepared would leave the page frozen with no indication why. The message names the
        // missing half rather than the symptom.
        Assert.Contains("PrepareAsync", ex.Message);
        Assert.Contains("always a pair", ex.Message);
    }

    [Fact]
    public async Task Preparing_PublishesTheHandoverSeam()
    {
        // The server runtime discovers a ready browser runtime by finding window.__raskWasmPaint and
        // nothing else. Publishing it is the framework's job rather than the app boot script's: there
        // are several page shells and an app may write its own, so a seam only some of them publish is
        // a takeover that silently never happens — with no error anywhere, just a page that keeps
        // paying a round trip per click for ever.
        JSInterop.ResetPublishPaintCount();

        var builder = WasmHostBuilder.CreateDefault();
        await builder.PrepareAsync<Blank>();

        Assert.Equal(1, JSInterop.PublishPaintCount);
    }

    private sealed class Blank : Component
    {
        protected override Component? Render() => null;
    }
}
