using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Components;
using Rask.Benchmarks.VsBlazor.Components;
using Rask.Benchmarks.VsBlazor.Infrastructure;
using Rask.Core;
using Rask.Core.Live;

namespace Rask.Benchmarks.VsBlazor.Reports;

/// <summary>
///     Deterministic "memory usage" report — the counterpart to the wire-bytes
///     <see cref="VsBlazorPayloadBytesReport" />. Emits two CSV blocks, both measured with
///     precise GC counters (no BenchmarkDotNet, no timing jitter):
///     <list type="number">
///         <item>
///             <b>Allocation per incremental update</b> — the production-relevant memory
///             number. Drives one framework's natural incremental-update path
///             (Rask: a stateful root whose cached rows survive, only the counter cell
///             re-renders; Blazor: an attached root re-rendered with one changed parameter)
///             and measures bytes allocated per update via
///             <c>GC.GetAllocatedBytesForCurrentThread()</c>. This is where Rask's
///             pooled-buffer diff codec wins — it allocates a small transient render tree +
///             reused diff buffers, where Blazor rebuilds and diffs a render batch each time.
///         </item>
///         <item>
///             <b>Retained heap per rendered tree</b> — the architectural tradeoff, NOT
///             Rask's production memory profile. Rask retains its <see cref="Component" />
///             object graph (one heap object per element) where Blazor packs dense
///             <c>RenderTreeFrame</c> structs, so a tree HELD in memory costs Rask more.
///             But Rask rebuilds-and-discards element trees per render in production rather
///             than retaining them — so this figure is the worst case "pin every node
///             forever," surfaced for completeness, not the number to lead with.
///         </item>
///     </list>
///     <para>
///         Together with the in-proc Scope 4 startup benches these stand in for the
///         "startup / memory footprint" story that a faithful in-browser Mono WASM startup
///         measurement can't give us in-process (see the Methodology section of
///         <c>Baselines/vs-blazor.md</c>).
///     </para>
///     <para>
///         Invoke: <c>dotnet run -c Release --project Rask.Benchmarks.VsBlazor -- mem-footprint</c>
///     </para>
/// </summary>
internal static class VsBlazorMemFootprintReport
{
    // Updates measured per scenario for the allocation pass (after warmup). Large enough
    // that the one-time attach cost amortises to ~0 per update.
    private const int Updates = 5_000;

    // Independent roots retained per scenario for the footprint pass.
    private const int Roots = 200;

    public static int Run(string[] args)
    {
        Console.WriteLine("# Allocation per incremental update (production-relevant; lower is better)");
        Console.WriteLine("Scenario,Updates,RaskAllocBytesPerUpdate,BlazorAllocBytesPerUpdate,RaskVsBlazor");
        ReportAllocCounterOnLargePage();

        Console.WriteLine();
        Console.WriteLine("# Retained heap per rendered tree (architectural tradeoff — Rask retains the");
        Console.WriteLine("# Component graph; production rebuilds-and-discards element trees per render)");
        Console.WriteLine("Scenario,Roots,RaskHeapBytesPerTree,BlazorHeapBytesPerTree,RaskVsBlazor");
        ReportFootprintLargePage();
        ReportFootprintKeyedList100();

        return 0;
    }

    // ---- Allocation per update -------------------------------------------------------

    private static void ReportAllocCounterOnLargePage()
    {
        // Rask: stateful root — cached rows survive, only the counter cell re-renders, and
        // the diff codec reuses its pooled buffers across updates (the production shape).
        using var rask = new RaskHarness();
#pragma warning disable RASK014
        var stateful = new StatefulLargePageWithCounter();
#pragma warning restore RASK014
        rask.SeedPrevious(stateful);
        // Warm up: first updates allocate the pooled buffers + cached rows once.
        for (var i = 0; i < 64; i++)
        {
            stateful.Tick();
            rask.RenderAndBuildDiffPayloadBytes(stateful);
        }

        var raskBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < Updates; i++)
        {
            stateful.Tick();
            rask.RenderAndBuildDiffPayloadBytes(stateful);
        }

        var raskPerUpdate = (GC.GetAllocatedBytesForCurrentThread() - raskBefore) / Updates;

        // Blazor: attach once, then re-render with one changed parameter per update. The
        // InlineDispatcher runs inline on this thread so allocation stays measurable here.
        using var blazor = new BlazorRenderBatchCapture();
        // Warm up (and amortise the one-time attach) by running a throwaway sustained pass.
        blazor.MeasureSustainedIncrementalUpdates<LargePageWithCounter.BlazorLargePageWithCounter>(
            64, i => CounterParams(i));

        var blazorBefore = GC.GetAllocatedBytesForCurrentThread();
        blazor.MeasureSustainedIncrementalUpdates<LargePageWithCounter.BlazorLargePageWithCounter>(
            Updates, i => CounterParams(i));
        var blazorPerUpdate = (GC.GetAllocatedBytesForCurrentThread() - blazorBefore) / Updates;

        EmitAllocRow("CounterOnLargePage", raskPerUpdate, blazorPerUpdate);
    }

    private static ParameterView CounterParams(int counter) =>
        ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(LargePageWithCounter.BlazorLargePageWithCounter.Counter)] = counter
        });

    // ---- Retained footprint ----------------------------------------------------------

    private static void ReportFootprintLargePage()
    {
#pragma warning disable RASK014 // benchmark-internal component, constructed directly
        var raskPerTree = MeasureRaskFootprint(() => new RaskLargePage());
#pragma warning restore RASK014
        var blazorPerTree = MeasureBlazorFootprint(renderer => () =>
            renderer.RenderAsRootAndMeasure<LargePageWithCounter.BlazorLargePageWithCounter>(
                CounterParams(0)));

        EmitFootprintRow("LargePage_200Rows", raskPerTree, blazorPerTree);
    }

    private static void ReportFootprintKeyedList100()
    {
        var order = new int[100];
        for (var i = 0; i < order.Length; i++)
        {
            order[i] = i;
        }

#pragma warning disable RASK014 // benchmark-internal component, constructed directly
        var raskPerTree = MeasureRaskFootprint(() => new RaskKeyedListPage());
#pragma warning restore RASK014
        var blazorPerTree = MeasureBlazorFootprint(renderer => () =>
            renderer.RenderAsRootAndMeasure<KeyedList.BlazorKeyedList>(
                ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    [nameof(KeyedList.BlazorKeyedList.Order)] = order
                })));

        EmitFootprintRow("KeyedList_100Rows", raskPerTree, blazorPerTree);
    }

    // Retained-representation-per-page, the production shape and apples-to-apples with the Blazor
    // side (a ComponentBase whose retained cost is its RenderTreeFrame[]). Each page is a user
    // component (what a real Rask page is) rendered once WITH a frame sink active, so Phase B's
    // clean-subtree cache kicks in: the page snapshots its rendered subtree as a compact RenderFrame
    // span on its LiveState and RELEASES the Element object graph. We retain only the page components
    // (holding those frame spans) — the throwaway sink/StringBuilder are dropped before measuring, the
    // same way the old measurement retained only the element graph, so the delta is a like-for-like
    // "what does a mounted page cost" comparison, now frames instead of a heap-object-per-element tree.
    private static long MeasureRaskFootprint(Func<Component> buildBody)
    {
        var roots = new List<Component>(Roots);
        var before = StableHeap();
        // Render in a separate frame so the transient sink + StringBuilder fall out of scope and are
        // reclaimed by StableHeap()'s forced GC before we measure — only the retained page components
        // (each now holding a compact frame span in place of its Element graph) count toward the delta.
        RenderFootprintPagesInto(roots, buildBody);
        var after = StableHeap();
        GC.KeepAlive(roots);
        return (after - before) / Roots;
    }

    private static void RenderFootprintPagesInto(List<Component> roots, Func<Component> newPage)
    {
        var sink = new FrameWriter();
        var sb = new StringBuilder();
        for (var i = 0; i < Roots; i++)
        {
            var page = newPage();
            sink.Reset();
            sb.Clear();
            using (FrameSinkScope.Push(sink))
            {
                HtmlSerializer.Serialize(page, sb);
            }

            roots.Add(page);
        }
    }

    // Render M Blazor roots into ONE renderer, retaining them via the renderer's root-component
    // table, so the delta is M render trees (not M renderers/circuits).
    private static long MeasureBlazorFootprint(Func<BlazorRenderBatchCapture, Action> renderOneRoot)
    {
        using var renderer = new BlazorRenderBatchCapture();
        var renderOne = renderOneRoot(renderer);
        var before = StableHeap();
        for (var i = 0; i < Roots; i++)
        {
            renderOne();
        }

        var after = StableHeap();
        GC.KeepAlive(renderer);
        return (after - before) / Roots;
    }

    // ---- Shared --------------------------------------------------------------------

    private static long StableHeap()
    {
        for (var i = 0; i < 3; i++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true);
            GC.WaitForPendingFinalizers();
        }

        return GC.GetTotalMemory(true);
    }

    private static void EmitAllocRow(string scenario, long raskPerUpdate, long blazorPerUpdate)
    {
        // Rask wins when it allocates less; report Blazor/Rask so >1 reads as a Rask win.
        var ratio = raskPerUpdate == 0
            ? "n/a"
            : (blazorPerUpdate / (double)raskPerUpdate).ToString("0.00", CultureInfo.InvariantCulture) + "x";
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture, "{0},{1},{2},{3},{4}",
            scenario, Updates, raskPerUpdate, blazorPerUpdate, ratio));
    }

    private static void EmitFootprintRow(string scenario, long raskPerTree, long blazorPerTree)
    {
        var ratio = raskPerTree == 0
            ? "n/a"
            : (blazorPerTree / (double)raskPerTree).ToString("0.00", CultureInfo.InvariantCulture) + "x";
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture, "{0},{1},{2},{3},{4}",
            scenario, Roots, raskPerTree, blazorPerTree, ratio));
    }
}
