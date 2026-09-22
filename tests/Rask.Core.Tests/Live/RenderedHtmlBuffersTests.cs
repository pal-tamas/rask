using System.Text;
using Rask.Core.Live;

namespace Rask.Core.Tests.Live;

public class RenderedHtmlBuffersTests
{
    private static RenderedHtmlBuffers WithCurrent(string html)
    {
        var b = new RenderedHtmlBuffers();
        b.CopyFrom(new StringBuilder(html));
        return b;
    }

    [Fact]
    public void Fresh_buffers_have_no_previous()
    {
        using var b = new RenderedHtmlBuffers();
        Assert.False(b.HasPrevious);
        // With no baseline a render can never dedup as a no-op — it must always be treated as changed.
        b.CopyFrom(new StringBuilder("<p>x</p>"));

        Assert.False(b.CurrentEqualsPrevious());
    }

    [Fact]
    public void CopyFrom_exposes_the_rendered_chars_as_current()
    {
        using var b = WithCurrent("<div>hello</div>");

        Assert.Equal("<div>hello</div>", b.CurrentSpan.ToString());
        Assert.Equal("<div>hello</div>", b.Current.ToString());
    }

    [Fact]
    public void A_commit_promotes_current_to_baseline_then_an_identical_render_dedups()
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
    public void A_changed_render_after_a_commit_does_not_dedup()
    {
        using var b = WithCurrent("<p>a</p>");
        b.Commit();
        b.CopyFrom(new StringBuilder("<p>b</p>"));

        Assert.False(b.CurrentEqualsPrevious());
    }

    [Fact]
    public void A_different_length_does_not_dedup()
    {
        using var b = WithCurrent("<p>a</p>");
        b.Commit();
        b.CopyFrom(new StringBuilder("<p>aa</p>"));

        Assert.False(b.CurrentEqualsPrevious());
    }

    [Fact]
    public void A_commit_is_a_zero_copy_swap_keeping_both_renders_distinct()
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
    public void Invalidating_drops_the_baseline_so_the_next_render_is_always_changed()
    {
        using var b = WithCurrent("<p>x</p>");
        b.Commit();
        b.Invalidate();
        Assert.False(b.HasPrevious);
        b.CopyFrom(new StringBuilder("<p>x</p>"));

        Assert.False(b.CurrentEqualsPrevious());
    }

    [Fact]
    public void SeedPrevious_sets_the_baseline_from_a_string_for_the_GET_render_handoff()
    {
        using var b = new RenderedHtmlBuffers();
        b.SeedPrevious("<html><head></head><body>x</body></html>");
        Assert.True(b.HasPrevious);
        // The first live update after the GET must dedup a byte-identical re-render against the seed.
        b.CopyFrom(new StringBuilder("<html><head></head><body>x</body></html>"));

        Assert.True(b.CurrentEqualsPrevious());
    }

    [Fact]
    public void The_buffers_grow_from_nothing_without_truncating()
    {
        using var b = new RenderedHtmlBuffers();
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

    [Fact]
    public void Fresh_buffers_are_empty_because_nothing_is_rented_until_first_use()
    {
        // The buffers are per-session and outlive every render, so they rent at the page's real size on
        // first use rather than pre-renting a fixed block for a page they haven't seen yet.
        using var b = new RenderedHtmlBuffers();

        Assert.Equal(0, b.CurrentSpan.Length);
        Assert.Equal(0, b.PreviousSpan.Length);
    }

    [Fact]
    public void Disposing_before_any_render_does_not_throw()
    {
        // A session can be torn down before it ever renders (an abandoned GET). Both buffers are still
        // the zero-length sentinel at that point, and handing those to the ArrayPool is not valid.
        var b = new RenderedHtmlBuffers();
        b.Dispose();
    }

    [Fact]
    public void Disposing_after_seeding_only_the_baseline_does_not_throw()
    {
        // The GET path seeds `previous` and may never render into `current`, so disposal has to cope with
        // one real rented buffer and one sentinel.
        var b = new RenderedHtmlBuffers();
        b.SeedPrevious("<html></html>");
        b.Dispose();
    }

    [Fact]
    public void Disposing_twice_is_idempotent()
    {
        var b = new RenderedHtmlBuffers();
        b.CopyFrom(new StringBuilder("<p>x</p>"));
        b.Dispose();
        // Returning the same array to the pool twice would let two sessions rent the same buffer.
        b.Dispose();
    }
}
