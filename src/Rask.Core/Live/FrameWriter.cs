using System.Buffers;
using System.Runtime.InteropServices;

namespace Rask.Core.Live;

/// <summary>
///     Writer for a <see cref="RenderFrame" /> stream. Owns a growable
///     <see cref="RenderFrame" /><c>[]</c> rented from <see cref="ArrayPool{T}" /> so
///     steady-state appends amortize to zero allocation across renders. The writer is
///     a regular class (not a <c>ref struct</c>) so <see cref="HtmlSerializer" /> can
///     thread an optional reference through its recursive call chain without ceremony.
/// </summary>
public sealed class FrameWriter : IDisposable
{
    // The pieces of the text run still open on frame _runFrame (-1: none). Every write and every read of
    // the frames ends the run first, so nothing ever sees a half-joined text.
    private readonly List<string> _runParts = [];
    private RenderFrame[] _buffer;
    private int _runFrame = -1;

    /// <summary>
    ///     The default starts small and lets <c>Reserve</c>'s doubling find the page's real size.
    ///     <para>
    ///         A live session retains two of these for its whole life (the diff baseline and the render in
    ///         flight), so the initial capacity is not scratch space — it is paid per concurrent user. The
    ///         old 256-frame default rented ~10 KB per writer, ~20 KB per session, before knowing whether
    ///         the page had ten nodes or ten thousand. Growth is amortized doubling, and the buffer is
    ///         reused across renders once it reaches the high-water mark, so a large page pays a handful of
    ///         grow-and-copy steps on its first render only and nothing thereafter. Pass an explicit
    ///         capacity when the frame count is known up front and the writer is short-lived.
    ///     </para>
    /// </summary>
    public FrameWriter(int initialCapacity = 16) =>
        _buffer = ArrayPool<RenderFrame>.Shared.Rent(Math.Max(16, initialCapacity));

    /// <summary>Total frames emitted so far.</summary>
    public int Count { get; private set; }

    /// <summary>
    ///     View over the emitted frames. Stable for the duration of one render
    ///     — invalidated by the next <c>Open*</c>/<c>Reset</c> call that triggers a resize.
    /// </summary>
    public ReadOnlySpan<RenderFrame> WrittenSpan
    {
        get
        {
            EndTextRun();
            return _buffer.AsSpan(0, Count);
        }
    }

    /// <summary>Reset the writer for the next render. Re-uses the underlying buffer.</summary>
    public void Reset()
    {
        DropTextRun();
        Count = 0;
    }

    /// <summary>
    ///     Open a regular HTML element. Returns the frame index so the caller
    ///     can pass it back to <see cref="CloseElement" /> to patch in the subtree length
    ///     and the HTML byte range.
    /// </summary>
    public int OpenElement(string tag, string? scopeId, bool selfClosing, int htmlStart, bool opaque = false)
    {
        var idx = Reserve();
        _buffer[idx] = new RenderFrame
        {
            Kind = RenderFrameKind.Element,
            Name = tag,
            Value = scopeId,
            SelfClosing = selfClosing,
            Opaque = opaque,
            SubtreeLength = 1,
            HtmlStart = htmlStart
        };
        return idx;
    }

    public void CloseElement(int openIndex, int htmlEnd)
    {
        EndTextRun();
        _buffer[openIndex].SubtreeLength = Count - openIndex;
        _buffer[openIndex].HtmlEnd = htmlEnd;
    }

    public int OpenComponent(Component instance, int htmlStart)
    {
        var idx = Reserve();
        _buffer[idx] = new RenderFrame
        {
            Kind = RenderFrameKind.Component,
            ComponentRef = instance,
            SubtreeLength = 1,
            HtmlStart = htmlStart
        };
        return idx;
    }

    public void CloseComponent(int openIndex, int htmlEnd) => CloseElement(openIndex, htmlEnd);

    public void Attribute(string name, string? value)
    {
        var idx = Reserve();
        _buffer[idx] = new RenderFrame
        {
            Kind = RenderFrameKind.Attribute,
            Name = name,
            Value = value,
            SubtreeLength = 1
        };
    }

    public void Text(string? value, int htmlStart, int htmlEnd)
    {
        // An empty text node emits no HTML and so produces NO DOM node — the browser never
        // creates one. Emitting a frame for it would make the diff count a node that isn't there
        // and drift every following sibling's domSlot path (same failure mode as adjacent text).
        // htmlStart == htmlEnd means AppendEncoded wrote nothing — i.e. value was null or "".
        if (htmlStart == htmlEnd)
        {
            return;
        }

        // The browser coalesces adjacent text into ONE DOM text node, so the frame model has to
        // as well — otherwise the diff's per-frame domSlot walk drifts past the real childNodes
        // and the UpdateText op targets a slot that doesn't exist (silently dropped → stale text).
        // HtmlEnd == htmlStart means nothing was emitted between the two texts: a tag, element, or
        // raw node would have advanced the StringBuilder, so contiguity is exactly DOM-adjacency.
        // This catches Fragment/Context-boundary adjacency too (they emit no HTML of their own).
        if (Count > 0)
        {
            ref var prev = ref _buffer[Count - 1];
            if (prev.Kind == RenderFrameKind.Text && prev.HtmlEnd == htmlStart)
            {
                // Joined once when the run ends (EndTextRun), not re-concatenated per piece — a run of n
                // pieces would otherwise build n-1 ever-longer strings.
                if (_runFrame != Count - 1)
                {
                    _runFrame = Count - 1;
                    _runParts.Add(prev.Name ?? string.Empty);
                }

                _runParts.Add(value ?? string.Empty);
                prev.HtmlEnd = htmlEnd;
                return;
            }
        }

        var idx = Reserve();
        _buffer[idx] = new RenderFrame
        {
            Kind = RenderFrameKind.Text,
            Name = value,
            SubtreeLength = 1,
            HtmlStart = htmlStart,
            HtmlEnd = htmlEnd
        };
    }

    public void Raw(string? value, int htmlStart, int htmlEnd)
    {
        var idx = Reserve();
        _buffer[idx] = new RenderFrame
        {
            Kind = RenderFrameKind.Raw,
            Name = value,
            SubtreeLength = 1,
            HtmlStart = htmlStart,
            HtmlEnd = htmlEnd
        };
    }

    public void Doctype(int htmlStart, int htmlEnd)
    {
        var idx = Reserve();
        _buffer[idx] = new RenderFrame
        {
            Kind = RenderFrameKind.Doctype,
            SubtreeLength = 1,
            HtmlStart = htmlStart,
            HtmlEnd = htmlEnd
        };
    }

    /// <summary>
    ///     Returns the frame buffer to the pool. A live session holds two of these for its whole life, so
    ///     without this their rentals were simply never given back — collected rather than reused, leaving
    ///     the pool to allocate a fresh array for the next session that came along.
    /// </summary>
    /// <remarks>
    ///     Cleared on return because a <see cref="RenderFrame" /> holds string references: handing the
    ///     array back dirty would keep a disposed session's tag and attribute strings alive for as long as
    ///     the pool held the array. Safe to call more than once, and safe to keep using the writer
    ///     afterwards — it re-rents on the next growth.
    /// </remarks>
    public void Dispose()
    {
        if (_buffer.Length > 0)
        {
            ArrayPool<RenderFrame>.Shared.Return(_buffer, true);
        }

        DropTextRun();
        _buffer = [];
        Count = 0;
    }

    private void EndTextRun()
    {
        if (_runFrame < 0)
        {
            return;
        }

        _buffer[_runFrame].Name = string.Concat(CollectionsMarshal.AsSpan(_runParts));
        DropTextRun();
    }

    private void DropTextRun()
    {
        _runFrame = -1;
        _runParts.Clear();
    }

    private int Reserve()
    {
        EndTextRun();
        if (Count == _buffer.Length)
        {
            // Math.Max keeps the doubling honest from a zero-length buffer, which is what a disposed
            // writer holds — otherwise Rent(0) would hand back an array that can never fit a frame.
            var bigger = ArrayPool<RenderFrame>.Shared.Rent(Math.Max(16, _buffer.Length * 2));
            Array.Copy(_buffer, bigger, Count);
            if (_buffer.Length > 0)
            {
                ArrayPool<RenderFrame>.Shared.Return(_buffer, true);
            }

            _buffer = bigger;
        }

        return Count++;
    }

    /// <summary>
    ///     Shift every recorded HTML offset at or past <paramref name="from" /> by
    ///     <paramref name="delta" />. Frame offsets are captured against the serialized HTML;
    ///     when <c>RenderAsLiveRootCore</c> later splices the head-asset sentinel out of (or
    ///     replaces it within) that HTML, every byte position after the splice moves, so the
    ///     offsets must move in lockstep — otherwise <c>FrameDiffer</c>'s <c>InsertSubtree</c>
    ///     fragment (sliced from the post-splice HTML via these offsets) reads the wrong bytes.
    /// </summary>
    public void AdjustOffsetsFrom(int from, int delta)
    {
        if (delta == 0)
        {
            return;
        }

        for (var i = 0; i < Count; i++)
        {
            if (_buffer[i].HtmlStart >= from)
            {
                _buffer[i].HtmlStart += delta;
            }

            if (_buffer[i].HtmlEnd >= from)
            {
                _buffer[i].HtmlEnd += delta;
            }
        }
    }
}
