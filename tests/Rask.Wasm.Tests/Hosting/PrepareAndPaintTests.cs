using Rask.Core;
namespace Rask.Wasm.Tests.Hosting;

// A takeover needs an app that is ready to render and does NOT render: arriving into a page another
// runtime is still driving, painting on arrival would put two runtimes into one document. PrepareAsync
// stops at that line and PaintAsync crosses it.
//
// Only the pairing guard is reachable here. Preparing for real imports the JS bridge and boots the
// .NET runtime, so "prepared but unpainted" is a browser fact and belongs in an E2E test once there is
// a takeover to drive it.
// Shares JSInterop's process-wide statics with everything else in this collection. A full boot
// rebinds _session, _runtime and _hostedServices, so running alongside a class that set them up for
// itself swaps the bindings out from under it — which surfaced as an unrelated JS round-trip test
// failing in the full suite and passing on its own.
[Collection("WasmSession")]
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

    [Fact]
    public async Task BootingIntoAServerDrivenPage_PreparesInsteadOfPainting()
    {
        // The DX the whole ladder rests on: one App class, one Program.cs, and no branch. Whether this
        // is a standalone WASM app on an empty page or a takeover arriving into a server-rendered one
        // depends on where it was loaded — which Program.cs cannot know. RunAsync reads the document's
        // owner and decides.
        //
        // Painting here is the failure being prevented: two runtimes writing one document, each
        // answering the same click, which presents as duplicated actions rather than as a boot problem.
        JSInterop.ResetPublishPaintCount();
        JSInterop.Owner = "server";
        try
        {
            var builder = WasmHostBuilder.CreateDefault();
            await builder.RunAsync<Blank>();

            // Publishing the seam is what a prepare does and a paint does not, so it is the observable
            // difference between the two paths.
            Assert.Equal(1, JSInterop.PublishPaintCount);
        }
        finally
        {
            JSInterop.Owner = string.Empty;
        }
    }

    [Fact]
    public async Task BootingIntoAPageNobodyOwns_Paints()
    {
        // The standalone case, and the reason the check reads an explicit owner rather than treating
        // any pre-existing markup as someone else's: every WASM app boots into a shell with a splash
        // screen in it, and mistaking that for a live runtime would leave every standalone app unpainted.
        JSInterop.ResetPublishPaintCount();

        var builder = WasmHostBuilder.CreateDefault();
        await builder.RunAsync<Blank>();

        Assert.Equal(0, JSInterop.PublishPaintCount);
    }

    private sealed class Blank : Component
    {
        protected override Component? Render() => null;
    }
}
