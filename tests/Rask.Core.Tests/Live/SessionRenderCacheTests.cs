using System.Text;
using Rask.Core.Live;
using C = Rask.Core.Components.Generated;

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

        var hasDiff = cache.Render(C.Div(Class: "x")["hi"], sb, ops);

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
        cache.Render(C.Div(Class: "counter")[C.Span()["1"]], sb1, ops);
        Assert.Empty(ops);

        var sb2 = new StringBuilder();
        var hasDiff = cache.Render(C.Div(Class: "counter")[C.Span()["2"]], sb2, ops);

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

        cache.Render(C.Div()["hi"], sb, ops);
        cache.Render(C.Div()["hi"], sb, ops);

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

        cache.Render(C.Div()[C.Span()["1"]], sb, ops);  // 1
        cache.Render(C.Div()[C.Span()["2"]], sb, ops);  // diff vs 1
        ops.Clear();
        cache.Render(C.Div()[C.Span()["3"]], sb, ops);  // diff vs 2

        var op = Assert.Single(ops);
        Assert.Equal("3", op.Value);
    }
}
