using Rask.Core.Live;

#pragma warning disable RASK014 // the tests need the very instance they hand to the render context

namespace Rask.Core.Tests.Builder;

// A joined class reports a prop change only when the FINAL list differs from the one the last render ended
// with. The first `.Class("a")` of a pass always differs from last pass's "a b", so judging each write on its
// own would dirty the component on every render: its update hook would run and the render cache would miss.
internal sealed partial class JoinedLeaf : Component
{
    public string? Class { get; set; }

    internal int PropsChanges;
    internal int Renders;

    protected override Task OnUpdated()
    {
        PropsChanges++;
        return Task.CompletedTask;
    }

    protected override Component? Render()
    {
        Renders++;
        return Span.Class(Class)["x"];
    }
}

internal sealed partial class JoinedHost : Component
{
    internal string Second = "b";
    internal bool KeyInTheMiddle;
    internal JoinedLeaf? Leaf;

    protected override Component? Render() =>
        Div[Leaf = KeyInTheMiddle
            ? JoinedLeaf.Class("a").Key(1).Class(Second)
            : JoinedLeaf.Class("a").Class(Second)];
}

// Many siblings in one Render(), each with its own joined class: the drain settles all of their watches together,
// and a change on one of them must still be told to that one alone.
internal sealed partial class JoinedSiblingsHost : Component
{
    internal string[] Seconds = ["b", "b", "b"];
    internal JoinedLeaf[] Leaves = new JoinedLeaf[3];

    protected override Component? Render()
    {
        var leaves = new Component[Seconds.Length];
        for (var i = 0; i < leaves.Length; i++)
        {
            leaves[i] = Leaves[i] = JoinedLeaf.Class("a").Key(i).Class(Seconds[i]);
        }

        return Div[leaves];
    }
}

// A big table built in one Render(): every cell opens a class watch on its first render.
internal sealed partial class WatchedTableHost : Component
{
    internal int Rows;

    protected override Component? Render() =>
        Table[Tbody[Enumerable.Range(0, Rows).Select(i => Tr.Key(i)[Cells()])]];

    private static Component[] Cells()
    {
        var cells = new Component[10];
        for (var c = 0; c < cells.Length; c++)
        {
            cells[c] = Td.Class("p-2").Class("truncate")["x"];
        }

        return cells;
    }
}

public class ClassJoinChangeTests
{
    private static string RenderLive(Component host, IServiceProvider sp)
    {
        using var ctx = LiveRenderContext.Begin(host, sp);
        var resolved = ctx.GetOrCreate(_ => host);
        LiveRenderContext.NotifyParameters(resolved, propsChanged: true);
        return resolved.ToHtml();
    }

    [Fact]
    public void A_joined_class_that_ends_where_it_did_last_render_reports_no_change()
    {
        var sp = RenderHarness.EmptyServices();
        var host = new JoinedHost();

        for (var i = 0; i < 6; i++)
        {
            RenderLive(host, sp);
        }

        Assert.Equal(1, host.Leaf!.PropsChanges);
        Assert.Equal(1, host.Leaf.Renders);
    }

    [Fact]
    public void A_joined_class_whose_second_part_moves_reports_the_change_once()
    {
        var sp = RenderHarness.EmptyServices();
        var host = new JoinedHost();
        RenderLive(host, sp);
        host.Second = "c";

        var html = RenderLive(host, sp);
        RenderLive(host, sp);

        Assert.Contains("<span class=\"a c\">", html, StringComparison.Ordinal);
        Assert.Equal(2, host.Leaf!.PropsChanges);
        Assert.Equal(2, host.Leaf.Renders);
    }

    [Fact]
    public void A_joined_class_split_around_Key_reports_no_change_when_it_ends_the_same()
    {
        var sp = RenderHarness.EmptyServices();
        var host = new JoinedHost { KeyInTheMiddle = true };

        for (var i = 0; i < 6; i++)
        {
            RenderLive(host, sp);
        }

        Assert.Equal(1, host.Leaf!.PropsChanges);
        Assert.Equal(1, host.Leaf.Renders);
    }

    [Fact]
    public void Of_many_joined_siblings_only_the_one_whose_class_moved_reports_a_change()
    {
        var sp = RenderHarness.EmptyServices();
        var host = new JoinedSiblingsHost();
        RenderLive(host, sp);
        host.Seconds = ["b", "c", "b"];

        var html = RenderLive(host, sp);

        Assert.Contains("<span class=\"a c\" data-rask-key=\"1\">", html, StringComparison.Ordinal);
        Assert.Equal([1, 2, 1], host.Leaves.Select(l => l.PropsChanges));
        Assert.Equal([1, 2, 1], host.Leaves.Select(l => l.Renders));
    }

    // Each cell's watch was settled by a scan of every watch the render opened, so a first render of n cells cost
    // n x n. Eight times the cells may take about eight times as long; a quadratic settle takes about sixty-four.
    [Fact]
    public void A_first_render_of_a_big_table_with_joined_classes_grows_linearly_with_its_cells()
    {
        var sp = RenderHarness.EmptyServices();
        RenderFirst(sp, 250);

        var small = RenderFirst(sp, 250);
        var large = RenderFirst(sp, 2_000);

        Assert.True(large < small * 24, $"20,000 cells took {large / small:F1}x as long as 2,500 (linear is ~8x).");
    }

    // The best of five, each on a host that has never rendered, so every cell opens its watch.
    private static double RenderFirst(IServiceProvider sp, int rows)
    {
        var best = double.MaxValue;
        for (var i = 0; i < 5; i++)
        {
            var host = new WatchedTableHost { Rows = rows };
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            RenderLive(host, sp);
            best = Math.Min(best, System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }

        return best;
    }
}
