using System.Text;
using Rask.Core.Live;

namespace Rask.Core.Tests.Live;

public class RenderedHtmlBuffersTests
{
    private static RenderedHtmlBuffers WithCurrent(string html)
    {
        var b = new RenderedHtmlBuffers(16);
        b.CopyFrom(new StringBuilder(html));
        return b;
    }

    [Fact]
    public void FreshBuffers_HaveNoPrevious()
    {
        using var b = new RenderedHtmlBuffers(16);
        Assert.False(b.HasPrevious);
        // With no baseline a render can never dedup as a no-op — it must always be treated as changed.
        b.CopyFrom(new StringBuilder("<p>x</p>"));
        Assert.False(b.CurrentEqualsPrevious());
    }

    [Fact]
    public void CopyFrom_ExposesTheRenderedCharsAsCurrent()
    {
        using var b = WithCurrent("<div>hello</div>");
        Assert.Equal("<div>hello</div>", b.CurrentSpan.ToString());
        Assert.Equal("<div>hello</div>", b.Current.ToString());
    }

    [Fact]
    public void Commit_PromotesCurrentToBaseline_ThenIdenticalRenderDedups()
    {
        using var b = WithCurrent("<p>same</p>");
        b.Commit();
        Assert.True(b.HasPrevious);
        Assert.Equal("<p>same</p>", b.PreviousSpan.ToString());

        // A byte-identical next render is a no-op that must dedup.
        b.CopyFrom(new StringBuilder("<p>same</p>"));
        Assert.True(b.CurrentEqualsPrevious());
    }

    [Fact]
    public void Commit_ThenChangedRender_DoesNotDedup()
    {
        using var b = WithCurrent("<p>a</p>");
        b.Commit();
        b.CopyFrom(new StringBuilder("<p>b</p>"));
        Assert.False(b.CurrentEqualsPrevious());
    }

    [Fact]
    public void DifferentLength_DoesNotDedup()
    {
        using var b = WithCurrent("<p>a</p>");
        b.Commit();
        b.CopyFrom(new StringBuilder("<p>aa</p>"));
        Assert.False(b.CurrentEqualsPrevious());
    }

    [Fact]
    public void Commit_IsAZeroCopySwap_KeepingBothRendersDistinct()
    {
        // First render committed as baseline; second render is current. Both must remain readable and
        // distinct — the swap must not alias current onto previous.
        using var b = WithCurrent("<p>one</p>");
        b.Commit();
        b.CopyFrom(new StringBuilder("<p>two</p>"));
        Assert.Equal("<p>two</p>", b.CurrentSpan.ToString());
        Assert.Equal("<p>one</p>", b.PreviousSpan.ToString());
    }

    [Fact]
    public void Invalidate_DropsBaseline_SoNextRenderIsAlwaysChanged()
    {
        using var b = WithCurrent("<p>x</p>");
        b.Commit();
        b.Invalidate();
        Assert.False(b.HasPrevious);
        b.CopyFrom(new StringBuilder("<p>x</p>"));
        Assert.False(b.CurrentEqualsPrevious());
    }

    [Fact]
    public void SeedPrevious_SetsBaselineFromAString_ForTheGetRenderHandoff()
    {
        using var b = new RenderedHtmlBuffers(16);
        b.SeedPrevious("<html><head></head><body>x</body></html>");
        Assert.True(b.HasPrevious);
        // The first live update after the GET must dedup a byte-identical re-render against the seed.
        b.CopyFrom(new StringBuilder("<html><head></head><body>x</body></html>"));
        Assert.True(b.CurrentEqualsPrevious());
    }

    [Fact]
    public void GrowsBeyondInitialCapacity_WithoutTruncating()
    {
        using var b = new RenderedHtmlBuffers(8);
        var big = new string('a', 5000);
        b.CopyFrom(new StringBuilder(big));
        Assert.Equal(5000, b.CurrentSpan.Length);
        Assert.Equal(big, b.CurrentSpan.ToString());

        b.Commit();
        var bigger = new string('b', 9000);
        b.CopyFrom(new StringBuilder(bigger));
        Assert.Equal(bigger, b.CurrentSpan.ToString());
        Assert.Equal(big, b.PreviousSpan.ToString());
    }
}
