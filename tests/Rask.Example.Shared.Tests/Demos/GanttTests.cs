using System.Text.Json;
using Rask.Example.Shared.Features;
using Rask.Example.Shared.Tests.Infrastructure;
using static Rask.Example.Shared.Features.Generated;

namespace Rask.Example.Shared.Tests.Demos;

// The Gantt wrapper drives frappe-gantt, so what's testable without a browser is the contract with it:
// the host stays a leaf, the right JS calls go out with the right arguments, the JSON matches what the
// library parses, and events route back to the owning instance. The chart actually drawing — and
// surviving a morph — is E2E's job (SharedSmokeTests.Journey).
public sealed partial class GanttTests : global::Rask.Core.RaskMarkup
{
    private static readonly GanttTask[] Tasks =
    [
        new("a", "Design", new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 6), 100),
        new("b", "Build", new DateOnly(2026, 3, 5), new DateOnly(2026, 3, 12), 40)
    ];

    // The load-bearing invariant: diff ops are addressed by positional path from the render tree, so a
    // host with no rendered children is a host the diff can never reach into. If this ever renders
    // children, the live diff starts fighting the library for ownership of the chart's DOM.
    [Fact]
    public void Host_RendersAsChildlessLeafCarryingTheRef()
    {
        var js = new FakeJsRuntime();
        var host = new LiveHost(() => Gantt.Data(Tasks), TestServices.Default(js: js));

        var html = host.RenderAsLiveRoot();

        Assert.Matches("""<div[^>]*class="rask-gantt"[^>]*></div>""", html);
        Assert.Contains("data-rask-ref=", html);
    }

    [Fact]
    public void FirstRender_MountsOnceWithRefIdPathBaseAndOptions()
    {
        var js = new FakeJsRuntime();
        var host = new LiveHost(() => Gantt.Data(Tasks), TestServices.Default(js: js));

        host.RenderAsLiveRoot();

        var call = Assert.Single(js.GetCalls("Rask.Gantt.mount"));
        Assert.NotNull(call);
        Assert.Equal(4, call!.Length);
        Assert.IsType<ElementRef>(call[0]);
        Assert.IsType<string>(call[1]);   // opaque token, not a countable id
        Assert.IsType<string>(call[2]);     // PathBase
        Assert.Contains("\"Design\"", Assert.IsType<string>(call[3]));
    }

    // Re-rendering must not re-mount: a second `new Gantt(host, ...)` would stack a second chart in the
    // same element.
    [Fact]
    public void ReRender_DoesNotMountAgain()
    {
        var js = new FakeJsRuntime();
        var host = new LiveHost(() => Gantt.Data(Tasks), TestServices.Default(js: js));

        host.RenderAsLiveRoot();
        host.RenderAsLiveRoot();
        host.RenderAsLiveRoot();

        Assert.Equal(1, js.CallCount("Rask.Gantt.mount"));
    }

    // Props are diffed with EqualityComparer<T>.Default — reference equality for a list. A caller who
    // mutates the same instance gets no OnPropsChanged and a chart that silently stops tracking its data
    // (the demo hit exactly this). Handing over a new list must reach the library.
    [Fact]
    public void NewDataReference_PushesAnUpdateToTheLibrary()
    {
        var js = new FakeJsRuntime();
        IReadOnlyList<GanttTask> tasks = Tasks;
        var host = new LiveHost(() => Gantt.Data(tasks), TestServices.Default(js: js));
        host.RenderAsLiveRoot();
        Assert.Equal(0, js.CallCount("Rask.Gantt.update"));

        // Same contents, same instance: nothing to push.
        host.RenderAsLiveRoot();
        Assert.Equal(0, js.CallCount("Rask.Gantt.update"));

        // A new list: the diff sees a changed prop and the new bar reaches the library.
        tasks = [.. Tasks, new GanttTask("c", "Ship", new DateOnly(2026, 3, 13), new DateOnly(2026, 3, 16), 0)];
        host.RenderAsLiveRoot();

        var update = Assert.Single(js.GetCalls("Rask.Gantt.update"));
        Assert.Contains("\"Ship\"", Assert.IsType<string>(update![1]));
    }

    // The mount round trip spans the library's own download. A prop change landing in that window saw
    // _mounted still false and was skipped, so the chart stayed on whatever it mounted with — forever.
    // The mount here never completes, which is exactly the window under test.
    [Fact]
    public async Task PropChangeDuringTheMountWindow_IsReconciledOnceMountCompletes()
    {
        var js = new FakeJsRuntime();
        var gate = new TaskCompletionSource();
        js.SetPending("Rask.Gantt.mount", gate.Task);

        IReadOnlyList<GanttTask> tasks = Tasks;
        var host = new LiveHost(() => Gantt.Data(tasks), TestServices.Default(js: js));
        host.RenderAsLiveRoot();

        // Still loading: nothing has been pushed yet.
        tasks = [.. Tasks, new GanttTask("c", "Ship", new DateOnly(2026, 3, 13), new DateOnly(2026, 3, 16), 0)];
        host.RenderAsLiveRoot();
        Assert.Equal(0, js.CallCount("Rask.Gantt.update"));

        // The library finishes loading — the change made in the meantime must not be lost.
        gate.SetResult();
        await WaitForAsync(() => js.CallCount("Rask.Gantt.update") > 0);

        var update = Assert.Single(js.GetCalls("Rask.Gantt.update"));
        Assert.Contains("\"Ship\"", Assert.IsType<string>(update![1]));
    }

    // The reconcile continues off the mount's completion, so it lands on another thread rather than
    // synchronously with SetResult.
    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(20);
        }

        Assert.True(condition(), "timed out waiting for the expected interop call");
    }

    // The token is a capability: the registry is static and shared across every Server session, and a
    // [JSInvokable] is callable by any script with any argument. A countable id would let one visitor
    // drive another's chart.
    [Fact]
    public void InteropTokens_AreUnguessable_NotSequential()
    {
        var chart = new Rask.Example.Shared.Features.Gantt(new FakeJsRuntime()) { Data = Tasks };
        var tokens = new List<string>();

        try
        {
            for (var i = 0; i < 5; i++)
            {
                tokens.Add(GanttInterop.Register(chart));
            }

            Assert.All(tokens, t => Assert.Equal(32, t.Length));
            Assert.Equal(tokens.Count, tokens.Distinct(StringComparer.Ordinal).Count());
            // The give-away of a countable id: small integers resolving to somebody's chart.
            Assert.All(tokens, t => Assert.False(int.TryParse(t, out _)));
        }
        finally
        {
            tokens.ForEach(GanttInterop.Unregister);
        }
    }

    [Fact]
    public void Unmount_DestroysTheChart()
    {
        var js = new FakeJsRuntime();
        var host = new LiveHost(() => Gantt.Data(Tasks), TestServices.Default(js: js));
        host.RenderAsLiveRoot();

        host.Mounted = false;
        host.RenderAsLiveRoot();

        Assert.Equal(1, js.CallCount("Rask.Gantt.destroy"));
    }

    // Two of frappe-gantt's view_mode strings contain a space, so no enum-name convention produces them.
    [Theory]
    [InlineData(GanttViewMode.Hour, "Hour")]
    [InlineData(GanttViewMode.QuarterDay, "Quarter Day")]
    [InlineData(GanttViewMode.HalfDay, "Half Day")]
    [InlineData(GanttViewMode.Day, "Day")]
    [InlineData(GanttViewMode.Week, "Week")]
    [InlineData(GanttViewMode.Month, "Month")]
    [InlineData(GanttViewMode.Year, "Year")]
    public void ViewMode_MapsToTheLibrarysOwnString(GanttViewMode mode, string expected) =>
        Assert.Equal(expected, Rask.Example.Shared.Features.Gantt.ToJsViewMode(mode));

    // A member added to the enum but not to the switch would fall into its default arm and silently
    // render as "Day". Distinctness catches that without needing this test updated for the new member.
    [Fact]
    public void EveryViewMode_MapsToADistinctString()
    {
        var mapped = Enum.GetValues<GanttViewMode>()
            .Select(Rask.Example.Shared.Features.Gantt.ToJsViewMode)
            .ToArray();

        Assert.Equal(mapped.Length, mapped.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void OptionsJson_MatchesTheShapeTheLibraryParses()
    {
        var js = new FakeJsRuntime();
        var chart = new Rask.Example.Shared.Features.Gantt(js)
        {
            Data = Tasks,
            Holidays = [new GanttHoliday(new DateOnly(2026, 3, 9), "Offsite")],
            ViewMode = GanttViewMode.QuarterDay
        };

        using var doc = JsonDocument.Parse(chart.BuildOptionsJson());
        var root = doc.RootElement;

        Assert.Equal("Quarter Day", root.GetProperty("viewMode").GetString());

        var first = root.GetProperty("tasks")[0];
        Assert.Equal("a", first.GetProperty("id").GetString());
        Assert.Equal("Design", first.GetProperty("name").GetString());
        Assert.Equal("2026-03-02", first.GetProperty("start").GetString());
        Assert.Equal("2026-03-06", first.GetProperty("end").GetString());
        Assert.Equal(100, first.GetProperty("progress").GetDouble());

        var holiday = root.GetProperty("holidays")[0];
        Assert.Equal("2026-03-09", holiday.GetProperty("date").GetString());
        Assert.Equal("Offsite", holiday.GetProperty("label").GetString());
    }

    [Fact]
    public void OptionsJson_EmitsAnEmptyHolidayArrayWhenNoneAreSet()
    {
        var chart = new Rask.Example.Shared.Features.Gantt(new FakeJsRuntime()) { Data = Tasks };

        using var doc = JsonDocument.Parse(chart.BuildOptionsJson());

        Assert.Empty(doc.RootElement.GetProperty("holidays").EnumerateArray());
    }

    [Fact]
    public async Task Interop_RoutesEachEventToTheOwningChart()
    {
        var clicked = "";
        var moved = "";
        var progress = -1d;
        var chart = new Rask.Example.Shared.Features.Gantt(new FakeJsRuntime())
        {
            Data = Tasks,
            OnTaskClick = id => { clicked = id; return Task.CompletedTask; },
            OnDateChange = e => { moved = $"{e.TaskId}:{e.Start:yyyy-MM-dd}:{e.End:yyyy-MM-dd}"; return Task.CompletedTask; },
            OnProgressChange = e => { progress = e.Progress; return Task.CompletedTask; }
        };
        var id = GanttInterop.Register(chart);

        try
        {
            await GanttInterop.TaskClicked(id, "a");
            // Gantt.js reports a full wall-clock timestamp; the wrapper models whole days.
            await GanttInterop.DateChanged(id, "a", "2026-03-04T00:00:00", "2026-03-08T23:59:59");
            await GanttInterop.ProgressChanged(id, "a", 42);

            Assert.Equal("a", clicked);
            Assert.Equal("a:2026-03-04:2026-03-08", moved);
            Assert.Equal(42, progress);
        }
        finally
        {
            GanttInterop.Unregister(id);
        }
    }

    // The registry is process-wide and static, so a missed Unregister leaks the component for the life of
    // the process — and on the Server host, across every session.
    [Fact]
    public async Task Interop_AfterUnregister_IsANoOp()
    {
        var calls = 0;
        var chart = new Rask.Example.Shared.Features.Gantt(new FakeJsRuntime())
        {
            Data = Tasks,
            OnTaskClick = _ => { calls++; return Task.CompletedTask; }
        };
        var id = GanttInterop.Register(chart);
        GanttInterop.Unregister(id);

        await GanttInterop.TaskClicked(id, "a");

        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Interop_ForAnUnknownId_DoesNotThrow() =>
        await GanttInterop.TaskClicked("no-such-token", "a");

    // The full lifecycle, driven through the framework: the id JS is given at mount must stop routing once
    // the component unmounts. Static registry + missed cleanup = the component leaks for the life of the
    // process, so this walks the real path rather than registering by hand.
    [Fact]
    public async Task Unmount_UnregistersSoLaterEventsAreDropped()
    {
        var calls = 0;
        var js = new FakeJsRuntime();
        var host = new LiveHost(
            () => Gantt.Data(Tasks).OnTaskClick(_ => { calls++; return Task.CompletedTask; }),
            TestServices.Default(js: js));

        host.RenderAsLiveRoot();
        var id = (string)Assert.Single(js.GetCalls("Rask.Gantt.mount"))![1]!;

        // Still live: the event routes.
        await GanttInterop.TaskClicked(id, "a");
        Assert.Equal(1, calls);

        host.Mounted = false;
        host.RenderAsLiveRoot();

        // Unmounted: a late event from JS finds nothing and is dropped.
        await GanttInterop.TaskClicked(id, "a");
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ProgressChange_ClampsToTheZeroToHundredRange()
    {
        var seen = new List<double>();
        var chart = new Rask.Example.Shared.Features.Gantt(new FakeJsRuntime())
        {
            Data = Tasks,
            OnProgressChange = e => { seen.Add(e.Progress); return Task.CompletedTask; }
        };

        await chart.HandleProgressChangeAsync("a", -5);
        await chart.HandleProgressChangeAsync("a", 150);

        Assert.Equal([0, 100], seen);
    }

    // A day is a wall-clock fact here: the user dropped the bar on a column. Neither the browser's zone
    // nor the server's may move it. The late-evening timestamp is the trap — under a naive parse a
    // Z-suffixed 23:59 rolls onto the next day for anyone east of UTC, so a bar dragged to the 8th comes
    // back as the 9th. These are the two shapes the boundary can actually see.
    [Theory]
    [InlineData("2026-03-08T23:59:59", "2026-03-08")]        // what Gantt.js sends: unzoned wall clock
    [InlineData("2026-03-08T23:59:59.000Z", "2026-03-08")]   // defensive: a zoned value must not shift
    public async Task DateChange_KeepsTheWallClockDay_RegardlessOfTimeZone(string sent, string expected)
    {
        var got = "";
        var chart = new Rask.Example.Shared.Features.Gantt(new FakeJsRuntime())
        {
            Data = Tasks,
            OnDateChange = e => { got = e.End.ToString("yyyy-MM-dd"); return Task.CompletedTask; }
        };

        await chart.HandleDateChangeAsync("a", "2026-03-04T00:00:00", sent);

        Assert.Equal(expected, got);
    }

    [Fact]
    public async Task DateChange_WithAnUnparseableDate_IsDropped()
    {
        var fired = false;
        var chart = new Rask.Example.Shared.Features.Gantt(new FakeJsRuntime())
        {
            Data = Tasks,
            OnDateChange = _ => { fired = true; return Task.CompletedTask; }
        };

        await chart.HandleDateChangeAsync("a", "not-a-date", "2026-03-08");

        Assert.False(fired);
    }
}
