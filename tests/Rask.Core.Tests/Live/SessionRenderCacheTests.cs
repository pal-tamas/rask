using System.Text;
using Rask.Core.Live;

namespace Rask.Core.Tests.Live;

// Verifies the round-trip: first render is "no diff" (full HTML), subsequent renders
// produce edit ops. The buffer rotation is steady-state allocation-free after warmup.
public class SessionRenderCacheTests
{
    [Fact]
    public void Render_FirstCall_ReturnsFalseAndPopulatesHtml()
    {
        var cache = new SessionRenderCache();
        var sb = new StringBuilder();
        var ops = new List<EditOp>();

        var hasDiff = cache.Render(Div(Class: "x")["hi"], sb, ops);

        Assert.False(hasDiff);
        Assert.Empty(ops);
        Assert.Contains("<div", sb.ToString());
    }

    [Fact]
    public void Render_SecondCall_ProducesDiffAgainstFirst()
    {
        var cache = new SessionRenderCache();
        var ops = new List<EditOp>();

        var sb1 = new StringBuilder();
        cache.Render(Div(Class: "counter")[Span()["1"]], sb1, ops);
        Assert.Empty(ops);

        var sb2 = new StringBuilder();
        var hasDiff = cache.Render(Div(Class: "counter")[Span()["2"]], sb2, ops);

        Assert.True(hasDiff);
        var op = Assert.Single(ops);
        Assert.Equal(EditOpKind.UpdateText, op.Kind);
        Assert.Equal("2", op.Value);
    }

    [Fact]
    public void Render_IdenticalRenders_ReturnsZeroOps()
    {
        var cache = new SessionRenderCache();
        var ops = new List<EditOp>();
        var sb = new StringBuilder();

        cache.Render(Div()["hi"], sb, ops);
        cache.Render(Div()["hi"], sb, ops);

        Assert.Empty(ops);
    }

    [Fact]
    public void Render_BuffersRotate_ThirdRenderDiffsAgainstSecondNotFirst()
    {
        // A→B→C: the third render's diff must be against B (the prior render), not A.
        // This proves the buffer rotation works — without it, the cache would keep
        // diffing against the original frame stream and miss subsequent transitions.
        var cache = new SessionRenderCache();
        var ops = new List<EditOp>();
        var sb = new StringBuilder();

        cache.Render(Div()[Span()["1"]], sb, ops); // 1
        cache.Render(Div()[Span()["2"]], sb, ops); // diff vs 1
        ops.Clear();
        cache.Render(Div()[Span()["3"]], sb, ops); // diff vs 2

        var op = Assert.Single(ops);
        Assert.Equal("3", op.Value);
    }

    // ---- coalescing (rotate:false) invariants ----
    // The WASM coalescing loop builds a payload several times within one dispatch but ships only
    // the last build, so it diffs every intermediate build against the stable last-sent baseline
    // (rotate:false) and Snapshot()s exactly once afterwards. These pin that contract.

    [Fact]
    public void TryComputeDiff_RotateFalse_KeepsDiffingAgainstStableBaseline()
    {
        var cache = new SessionRenderCache();
        var ops = new List<EditOp>();

        // Baseline "a" (first render rotates to establish it).
        Assert.False(RenderInto(cache, Div()["a"], ops, true));

        // Two intermediate builds, neither rotating — both diff against "a".
        Assert.True(RenderInto(cache, Div()["b"], ops, false));
        Assert.NotEmpty(ops);
        Assert.True(RenderInto(cache, Div()["c"], ops, false));
        Assert.NotEmpty(ops);

        // The baseline must still be "a": rendering "a" now (rotate:true) yields zero ops. If a
        // rotate:false call had wrongly promoted _current, the baseline would be "b"/"c" and this
        // would show a spurious diff.
        Assert.True(RenderInto(cache, Div()["a"], ops, true));
        Assert.Empty(ops);
    }

    [Fact]
    public void Snapshot_AfterRotateFalse_CommitsTheLastBuildAsBaseline()
    {
        var cache = new SessionRenderCache();
        var ops = new List<EditOp>();

        RenderInto(cache, Div()["a"], ops, true); // baseline a
        RenderInto(cache, Div()["b"], ops, false); // intermediate, no rotate
        cache.Snapshot(); // commit "b" exactly once

        // Baseline is now "b": rendering "b" yields no diff; rendering "a" would.
        Assert.True(RenderInto(cache, Div()["b"], ops, true));
        Assert.Empty(ops);
    }

    [Fact]
    public void TryComputeDiff_OnFalseReturn_StillRotates_SoNoDoubleRotateIsNeeded()
    {
        // The invariant TryComputeDiff rotates on EVERY call (true or false): a first render
        // returns false but must still establish the baseline. A following identical render then
        // diffs against it (zero ops) — proving the false-return path rotated.
        var cache = new SessionRenderCache();
        var ops = new List<EditOp>();

        Assert.False(RenderInto(cache, Div()["x"], ops, true)); // false, but rotates
        Assert.True(RenderInto(cache, Div()["x"], ops, true)); // has baseline now
        Assert.Empty(ops);
    }

    private static bool RenderInto(SessionRenderCache cache, Component tree, List<EditOp> ops, bool rotate)
    {
        var sb = new StringBuilder();
        using (FrameSinkScope.Push(cache.PrepareCurrentBuffer()))
        {
            HtmlSerializer.Serialize(tree, sb);
        }

        return cache.TryComputeDiff(ops, rotate);
    }
}
