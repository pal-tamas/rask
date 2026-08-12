using System.Globalization;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;
using Rask.Server;
using Bench = Rask.Benchmarks.Infrastructure.Generated;

namespace Rask.Benchmarks.Infrastructure;

/// <summary>
///     Shared plumbing for the <c>session-footprint</c> and <c>session-churn</c> reports: builds a
///     production-shaped host, mints live sessions against it, and drives them to a steady state —
///     all in-process, with no HTTP and no real socket.
/// </summary>
[global::Rask.Core.RaskMarkup]
internal static partial class SessionHarness
{
    /// <summary>
    ///     Updates driven to reach steady state. Two sends is the minimum for BOTH payload buffers to
    ///     reach their high-water mark: the first send swaps the page-sized <c>_writeBuffer</c> into
    ///     <c>_lastSentBuffer</c> and installs a fresh 4 KB one, which only grows on the send after it.
    ///     A third leaves margin.
    /// </summary>
    public const int UpdatesToSteadyState = 3;

    /// <summary>
    ///     A host container built exactly as production builds it — <c>AddRask</c> registers the
    ///     <see cref="LiveSessionStore" /> singleton, the ~15 scoped services a session's DI scope
    ///     carries, and <c>DiffMode.Auto</c> (so the diff codec, and therefore the
    ///     <c>SessionRenderCache</c> buffers, are part of the measured cost). An empty
    ///     <c>ServiceCollection</c> would under-count the scoped tail.
    /// </summary>
    public static ServiceProvider NewHost() => new ServiceCollection().AddRask().BuildServiceProvider();

    /// <summary>
    ///     Creates one session the way the GET endpoint does (<c>store.Create</c> wraps the app in a
    ///     <c>RootErrorBoundary</c> and wires the id/scope/accessor), renders the initial root, and — when
    ///     <paramref name="connected" /> — attaches a stub socket and drives it to steady state.
    ///     The store retains the session; the returned handle is for measurement and teardown.
    /// </summary>
    public static SessionHandle Create(LiveSessionStore store, int rows, bool connected)
    {
        FootprintApp? app = null;
        var session = store.Create(_ =>
        {
            app = FootprintApp.RowCount(rows);
            // Mirrors the GET endpoint, which wraps the user's App in this same implicit boundary
            // (RaskEndpointExtensions). RootErrorBoundary is framework-internal and has no public
            // factory, so this is the only way to reproduce production's real tree shape — and the
            // wrapper is part of what every session retains.
#pragma warning disable RASK014
            return new RootErrorBoundary(app);
#pragma warning restore RASK014
        });

        // The GET render: seeds the dedup baseline and, with the diff codec on, the frame baseline.
        var html = session.RenderInitialRoot();
        var pageHtmlBytes = Encoding.UTF8.GetByteCount(html);
        if (!connected)
        {
            return new SessionHandle(session, app!, pageHtmlBytes, 0);
        }

        var socket = new NullWebSocket();
        session.AttachSocket(socket, CancellationToken.None);
        Drive(session, app!, UpdatesToSteadyState);

        return new SessionHandle(session, app!, pageHtmlBytes, socket.BytesSent);
    }

    /// <summary>
    ///     Tears a session down through the store's own removal funnel — the same path the socket-close
    ///     grace period takes in production, which detaches it from the store and disposes the component
    ///     tree and DI scope.
    /// </summary>
    public static void Remove(LiveSessionStore store, SessionHandle handle) =>
        store.RemoveAsync(handle.Session.Id).GetAwaiter().GetResult();

    /// <summary>
    ///     Drives <paramref name="updates" /> render→send cycles. Each bumps the counter first:
    ///     <c>LiveSession.RenderAndSendAsync</c> early-returns before building a payload when the render
    ///     matches the baseline, so a session driven without a real state change would never allocate its
    ///     payload buffers and would report a footprint no real session has.
    /// </summary>
    public static void Drive(LiveSession session, FootprintApp app, int updates)
    {
        for (var i = 0; i < updates; i++)
        {
            app.Bump();
            session.RequestRenderAsync().GetAwaiter().GetResult();
        }
    }

    /// <summary>
    ///     As <see cref="Drive" />, but each update also flips the header's extra button, so the number of
    ///     handlers registered above the rows changes on every render. Drives the case a page hits whenever
    ///     a conditional action appears — and the one that decides whether the rows' cached subtrees can be
    ///     replayed or have to be re-walked.
    /// </summary>
    public static void DriveWithHandlerShift(LiveSession session, FootprintApp app, int updates)
    {
        for (var i = 0; i < updates; i++)
        {
            app.BumpWithHandlerShift();
            session.RequestRenderAsync().GetAwaiter().GetResult();
        }
    }

    /// <summary>
    ///     Three forced blocking gen-2 collections, then the live bytes.
    ///     <para>
    ///         Deliberately NOT the same technique as the vs-Blazor <c>mem-footprint</c> report, which
    ///         reads <c>GC.GetTotalMemory(true)</c> — see the note below on why that over-reports.
    ///         Numbers from the two reports are therefore not directly comparable in absolute terms;
    ///         that report's retained-heap figures lead with a Rask-vs-Blazor <i>ratio</i>, which the
    ///         inflation cancels out of.
    ///     </para>
    /// </summary>
    public static long StableHeap()
    {
        for (var i = 0; i < 3; i++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
            GC.WaitForPendingFinalizers();
        }

        // Sum the LIVE bytes per generation rather than calling GC.GetTotalMemory.
        //
        // GetTotalMemory reports the heap's committed size, which still includes the empty ephemeral
        // space left behind by the allocations that got us here — and that space scales with how much
        // was allocated. The effect is not subtle: measured against known-size controls it reports
        // EXACTLY 2x for every small-object allocation (a byte[16384] whose true cost is 16,408 comes
        // back as 32,815) while reporting large-object allocations exactly right, because those bypass
        // gen0. A per-session footprint built on it would be ~2x too pessimistic.
        //
        // GenerationInfo's SizeAfterBytes is the live data per generation after the collection above,
        // with no committed-but-free space in it, and it reproduces the controls to within a rounding
        // error.
        var info = GC.GetGCMemoryInfo();
        long live = 0;
        foreach (var gen in info.GenerationInfo)
        {
            live += gen.SizeAfterBytes;
        }

        return live;
    }

    /// <summary>
    ///     Verifies <see cref="StableHeap" /> against a known-size allocation before any real number is
    ///     reported, and throws if it is off by more than 5%.
    ///     <para>
    ///         This is not paranoia. The obvious implementation of <see cref="StableHeap" /> —
    ///         <c>GC.GetTotalMemory(true)</c> after a forced collection — over-reports every small-object
    ///         allocation by <b>exactly 2x</b>, because the heap's committed size still includes the empty
    ///         ephemeral space the allocations left behind. It reports large-object allocations correctly,
    ///         which is what makes the error so easy to miss: spot-check it with a big array and it looks
    ///         perfect. Every figure this harness produces is a small-object measurement, so a regression
    ///         here would silently double the whole report. Fail loudly instead.
    ///     </para>
    /// </summary>
    public static void VerifySelfMeasurement()
    {
        const int count = 200;
        const int arrayBytes = 16 * 1024;
        // byte[16384]: 16,384 payload + 24 bytes of array header, and small enough to live on the
        // small-object heap — the same heap every buffer in a live session lives on.
        const long expectedEach = arrayBytes + 24;

        var sink = new List<byte[]>(count);
        sink.Add(new byte[arrayBytes]); // warm up before the baseline
        var before = StableHeap();
        for (var i = 0; i < count; i++)
        {
            sink.Add(new byte[arrayBytes]);
        }

        var measured = (StableHeap() - before) / count;
        GC.KeepAlive(sink);

        var driftPercent = Math.Abs(measured - expectedEach) * 100.0 / expectedEach;
        if (driftPercent > 5.0)
        {
            throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture,
                "Heap measurement is unsound: a byte[{0}] should retain ~{1} bytes but measured {2} " +
                "({3:0.0}% off). Every number this report prints would be wrong by about that factor. " +
                "If StableHeap was changed to GC.GetTotalMemory, that is the cause — it counts committed " +
                "ephemeral space and doubles small-object measurements.",
                arrayBytes, expectedEach, measured, driftPercent));
        }
    }

    /// <summary>
    ///     Guards against the failure mode that would silently invalidate every number here: a session
    ///     that never reached steady state because no frame ever went out.
    /// </summary>
    public static void EnsureReachedSteadyState(SessionHandle handle)
    {
        if (handle.BytesSent == 0)
        {
            throw new InvalidOperationException(
                "Connected session sent no frames — it never reached steady state, so its footprint " +
                "would be under-measured. Check that the socket attached and that Bump() actually " +
                "changed the rendered HTML (RenderAndSendAsync dedups identical renders).");
        }
    }

    /// <summary>
    ///     A created session plus what the reports need from it: the app instance (to mutate state so a
    ///     render survives the dedup), the page's rendered size, and the bytes its socket actually
    ///     received (zero proves it never reached steady state).
    /// </summary>
    internal readonly record struct SessionHandle(
        LiveSession Session, FootprintApp App, int PageHtmlBytes, long BytesSent);
}
