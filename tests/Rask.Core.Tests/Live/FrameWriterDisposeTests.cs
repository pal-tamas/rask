using Rask.Core.Live;

namespace Rask.Core.Tests.Live;

/// <summary>
/// <see cref="FrameWriter"/> rents its frame buffer from <see cref="System.Buffers.ArrayPool{T}"/> and a
/// live session holds two of them for its whole life, so it needs a way to give them back.
/// </summary>
/// <remarks>
/// The failure mode worth guarding is not the leak — that was merely wasteful — but the fix's own hazard:
/// returning the same array twice hands one array to two owners, and the corruption surfaces later, in
/// whichever unrelated session was unlucky enough to rent it.
/// </remarks>
public sealed class FrameWriterDisposeTests
{
    private static FrameWriter Written(int frames)
    {
        var writer = new FrameWriter();
        for (var i = 0; i < frames; i++)
        {
            writer.OpenElement("div", null, false, i);
        }

        return writer;
    }

    [Fact]
    public void Dispose_clears_the_writer()
    {
        var writer = Written(5);
        Assert.Equal(5, writer.Count);

        writer.Dispose();

        Assert.Equal(0, writer.Count);
        Assert.Equal(0, writer.WrittenSpan.Length);
    }

    /// <summary>Double-dispose must not return the same array twice — that corrupts the shared pool.</summary>
    [Fact]
    public void Dispose_is_idempotent()
    {
        var writer = Written(3);

        writer.Dispose();
        writer.Dispose();
        writer.Dispose();

        Assert.Equal(0, writer.Count);
    }

    /// <summary>
    /// A disposed writer holds a zero-length buffer, so the growth path has to re-rent rather than double
    /// zero forever. Reachable through the render cache, whose writers are disposed and could in principle
    /// be handed another render.
    /// </summary>
    [Fact]
    public void A_disposed_writer_still_works_if_used_again()
    {
        var writer = Written(2);
        writer.Dispose();

        for (var i = 0; i < 50; i++)
        {
            writer.OpenElement("span", null, false, i);
        }

        Assert.Equal(50, writer.Count);
        Assert.Equal(50, writer.WrittenSpan.Length);

        writer.Dispose();
    }

    /// <summary>Growth past the initial rental must not double-return the buffer it replaced.</summary>
    [Fact]
    public void Growing_then_disposing_returns_only_the_live_buffer()
    {
        var writer = new FrameWriter();

        // Well past the 16-frame initial rental, so Reserve has grown (and returned) several times.
        for (var i = 0; i < 500; i++)
        {
            writer.OpenElement("li", null, false, i);
        }

        Assert.Equal(500, writer.Count);
        writer.Dispose();
        writer.Dispose();

        Assert.Equal(0, writer.Count);
    }

    /// <summary>The session-level owner has to release both writers, not just drop them.</summary>
    [Fact]
    public void The_render_cache_releases_its_writers()
    {
        var cache = new SessionRenderCache();
        var current = cache.PrepareCurrentBuffer();
        current.OpenElement("div", null, false, 0);

        cache.Dispose();
        cache.Dispose();

        Assert.Equal(0, current.Count);
    }
}
