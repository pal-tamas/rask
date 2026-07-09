using System.Buffers;
using System.Text;

namespace Rask.Core.Live;

/// <summary>
///     Double-buffered holder for a live session's rendered page HTML. It exists to keep the
///     per-update cost of the diff path off the GC: previously every render materialised the whole
///     page as a fresh <see cref="string" /> via <c>StringBuilder.ToString()</c> — the dominant
///     managed allocation of a small live update on a large page — purely so the session could
///     (a) dedup a byte-identical no-op render, (b) head-compare for the diff-vs-full decision, and
///     (c) slice an <c>InsertSubtree</c> fragment. All three are read-only over the chars, so we
///     render into a reused <see cref="char" /><c>[]</c> and hand out <see cref="ReadOnlyMemory{T}" />
///     / <see cref="ReadOnlySpan{T}" /> instead.
///     <para>
///         Two arrays ping-pong: <c>_cur</c> receives the just-rendered page; <c>_prev</c> is the
///         baseline the last <em>applied</em> render left behind. <see cref="Commit" /> swaps them
///         after a successful send, exactly mirroring the existing double-buffers for the wire bytes
///         (<c>_writeBuffer</c>/<c>_lastSentBuffer</c>) and the frame streams
///         (<see cref="SessionRenderCache" />). A session renders and consumes single-threaded under
///         its render lock, so the buffers never overlap two renders.
///     </para>
/// </summary>
internal sealed class RenderedHtmlBuffers : IDisposable
{
    private char[] _cur;
    private char[] _prev;
    private int _curLen;
    private int _prevLen;

    public RenderedHtmlBuffers(int initialCapacity = 8 * 1024)
    {
        _cur = ArrayPool<char>.Shared.Rent(initialCapacity);
        _prev = ArrayPool<char>.Shared.Rent(initialCapacity);
    }

    /// <summary>True once at least one render has been committed as the baseline.</summary>
    public bool HasPrevious { get; private set; }

    /// <summary>The just-rendered page, valid until the next <see cref="CopyFrom" />.</summary>
    public ReadOnlyMemory<char> Current => _cur.AsMemory(0, _curLen);

    /// <summary>Span view of <see cref="Current" />.</summary>
    public ReadOnlySpan<char> CurrentSpan => _cur.AsSpan(0, _curLen);

    /// <summary>The last committed (applied) render, or empty when <see cref="HasPrevious" /> is false.</summary>
    public ReadOnlySpan<char> PreviousSpan => _prev.AsSpan(0, _prevLen);

    /// <summary>Copy the freshly serialized page out of <paramref name="sb" /> into the current buffer.</summary>
    public void CopyFrom(StringBuilder sb)
    {
        var len = sb.Length;
        EnsureCapacity(ref _cur, len);
        sb.CopyTo(0, _cur, 0, len);
        _curLen = len;
    }

    /// <summary>True when the current render is byte-identical to the committed baseline (a no-op render).</summary>
    public bool CurrentEqualsPrevious() => HasPrevious && CurrentSpan.SequenceEqual(PreviousSpan);

    /// <summary>Promote the current render to the baseline for the next dedup / head-compare (zero-copy swap).</summary>
    public void Commit()
    {
        (_cur, _prev) = (_prev, _cur);
        (_curLen, _prevLen) = (_prevLen, _curLen);
        HasPrevious = true;
    }

    /// <summary>
    ///     Drop the baseline so the next render is treated as changed (used on reconnect resend, where the
    ///     freshly attached socket must receive a full catch-up frame regardless of byte equality).
    /// </summary>
    public void Invalidate() => HasPrevious = false;

    /// <summary>
    ///     Seed the baseline from an already-materialised HTML string — the initial GET render, whose
    ///     string is produced through the ordinary <c>ToString</c> path and shipped in the HTTP body, so
    ///     the first live update can dedup/head-compare against it.
    /// </summary>
    public void SeedPrevious(ReadOnlySpan<char> html)
    {
        EnsureCapacity(ref _prev, html.Length);
        html.CopyTo(_prev);
        _prevLen = html.Length;
        HasPrevious = true;
    }

    public void Dispose()
    {
        ArrayPool<char>.Shared.Return(_cur);
        ArrayPool<char>.Shared.Return(_prev);
        _cur = _prev = Array.Empty<char>();
        _curLen = _prevLen = 0;
    }

    private static void EnsureCapacity(ref char[] buffer, int needed)
    {
        if (buffer.Length >= needed)
        {
            return;
        }

        ArrayPool<char>.Shared.Return(buffer);
        buffer = ArrayPool<char>.Shared.Rent(needed);
    }
}
