using Xunit.Abstractions;

namespace Rask.Core.Tests.Performance;

// Quantifies Counter render allocation against the Blazor 1.46x loss baseline from
// Rask.Benchmarks.VsBlazor/Benchmarks/RenderHotPathBenchmarks.cs (Counter). Splits the
// allocation into "page shell" (Fragment > Doctype + Html > Body) and "inner div"
// contributions so we can see what's framework cost vs scenario shape.
//
// Pinned ceilings catch regression in CI; the diagnostic Trait test prints exact deltas
// for manual inspection. Run the diagnostic via:
//   dotnet test Rask.Core.Tests --filter "FullyQualifiedName~CounterAllocation_Diagnostic"
public class CounterAllocationPinTests
{
    private readonly ITestOutputHelper _output;

    public CounterAllocationPinTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void CounterRender_InnerOnly_StaysUnderPinnedCeiling()
    {
        // Match RenderHotPath_CounterBenchmarks: build + render in one call (the benchmark
        // doesn't pre-build, so the tree-construction allocations count).
        WarmUp(BuildInner);

        const int iterations = 1000;
        var perIterationBytes = MeasureAvgAllocBytes(BuildInner, iterations);

        // Blazor allocates ~3.37 KB / iter on the equivalent (no-shell) tree; Rask measured
        // at ~1.15 KB on 2026-05-27 after the LiveState hoist + the lazy-alloc series, drifting
        // to ~1.39 KB by 2026-06-08. The universal `Ref:` element-ref parameter (a nullable
        // ElementRef reference — class, not struct, precisely so it stays a cheap reference-type
        // optional param) adds ~24 B, to ~1.42 KB (~1496 B). The universal accessibility params
        // (`Role`/`TabIndex`/`Aria`) add ~72 B more, to ~1568 B: their storage is hoisted into the
        // lazy LiveState (like Ref) so an a11y-free element grows no Element instance, but these
        // elements allocate a LiveState during render regardless, so the three extra LiveState
        // fields ride along. Splitting every element's drag/keyboard handlers — and Div's scroll
        // handler — into typed sync `OnXxx` + async `OnXxxAsync` pairs (replacing the single
        // untyped `Delegate?` slots) added 7-8 extra typed delegate parameters to each element
        // factory; the generated factory's per-parameter change-detection bookkeeping (the `__old`
        // snapshot + EqualityComparer compare it emits for every public delegate prop) scales with
        // that count, adding ~150 B to ~1768 B. No element instance grew — the pairs coalesce over
        // a single backing slot per event by delegate type. Pin at <= 1.85 KB to catch regression
        // while leaving slack for jitter.
        Assert.InRange(perIterationBytes, 0, 1850);
    }

    [Fact(Skip = "diagnostic — run manually to gather allocation deltas")]
    public void CounterAllocation_Diagnostic_PrintDeltaBetweenShellAndInner()
    {
        WarmUp(BuildFullPageShell);
        WarmUp(BuildInner);

        const int iterations = 10_000;
        var fullPage = MeasureAvgAllocBytes(BuildFullPageShell, iterations);
        var inner = MeasureAvgAllocBytes(BuildInner, iterations);

        _output.WriteLine($"Counter full-page shell (Fragment > Doctype + Html > Body > Div): {fullPage} B/iter");
        _output.WriteLine($"Counter inner only (just the Div + its children): {inner} B/iter");
        _output.WriteLine($"Shell-wrapping overhead: {fullPage - inner} B/iter");
    }

    private static Component BuildFullPageShell() =>
        Fragment()[
            Doctype(),
            Html()[
                Body()[
                    Div(Class: "counter", Id: "counter")[
                        Span(Class: "value")["42"],
                        Button(Class: "inc")["+"]
                    ]
                ]
            ]
        ];

    private static Component BuildInner() =>
        Div(Class: "counter", Id: "counter")[
            Span(Class: "value")["42"],
            Button(Class: "inc")["+"]
        ];

    private static void WarmUp(Func<Component> build)
    {
        for (var i = 0; i < 100; i++)
        {
            _ = build().ToHtml();
        }
    }

    private static long MeasureAvgAllocBytes(Func<Component> build, int iterations)
    {
        // Per-thread allocation counter is monotonic and unaffected by other CPU activity,
        // so this co-exists with a parallel BDN run without skew. Force a GC first so the
        // measurement window starts on a clean LOH/POH state.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++)
        {
            _ = build().ToHtml();
        }

        var after = GC.GetAllocatedBytesForCurrentThread();

        return (after - before) / iterations;
    }
}
