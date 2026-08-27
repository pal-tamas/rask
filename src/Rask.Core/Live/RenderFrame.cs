using System.Buffers;

namespace Rask.Core.Live;

/// <summary>
///     Tag distinguishing the role of a frame in a render-tree stream. The stream is
///     emitted by <see cref="HtmlSerializer.Serialize(Component, System.Text.StringBuilder)" />
///     alongside the rendered HTML and consumed by <see cref="FrameDiffer" /> when
///     comparing successive renders to emit a minimal edit-op payload.
/// </summary>
public enum RenderFrameKind : byte
{
    /// <summary>
    ///     Opens an HTML element. The matching closing tag is implicit at
    ///     <c>index + SubtreeLength</c>; there is no <c>CloseElement</c> frame.
    /// </summary>
    Element = 1,

    /// <summary>A single name/value attribute on the most-recently-opened element.</summary>
    Attribute = 2,

    /// <summary>
    ///     HTML-encoded text content (the producer is responsible for any encoding
    ///     decisions — the frame stores the raw user-supplied text so a consumer can
    ///     re-encode if it writes to a different sink).
    /// </summary>
    Text = 3,

    /// <summary>Verbatim markup (no encoding). Corresponds to <see cref="Rask.Core.Components.Raw" />.</summary>
    Raw = 4,

    /// <summary>Doctype declaration.</summary>
    Doctype = 5,

    /// <summary>
    ///     Marks the start of a user-component's rendered subtree. The component
    ///     instance reference lets the diff codec short-circuit when an unchanged component
    ///     instance still produces an identical cached subtree.
    /// </summary>
    Component = 6
}

/// <summary>
///     Compact tagged-union over the render-tree frame variants. One value type per
///     element / attribute / text / component-marker; consumers walk a contiguous
///     <see cref="Span{T}" /> of frames and use <see cref="SubtreeLength" /> on
///     <see cref="RenderFrameKind.Element" /> / <see cref="RenderFrameKind.Component" />
///     frames to skip subtrees without recursion. Modelled on Blazor's
///     <c>RenderTreeFrame</c> but trimmed to the variants Rask actually diffs.
/// </summary>
public struct RenderFrame
{
    public RenderFrameKind Kind;

    /// <summary>
    ///     For <see cref="RenderFrameKind.Element" /> and
    ///     <see cref="RenderFrameKind.Component" />: total frames in the subtree rooted at
    ///     this frame including itself. <c>1</c> means a leaf element with no children. The
    ///     field is patched in by <see cref="FrameWriter.CloseElement" /> /
    ///     <see cref="FrameWriter.CloseComponent" /> at close time; while the element is
    ///     still open the value is meaningless.
    /// </summary>
    public int SubtreeLength;

    /// <summary>
    ///     Element tag (for <see cref="RenderFrameKind.Element" />), attribute name
    ///     (for <see cref="RenderFrameKind.Attribute" />), or text content (for
    ///     <see cref="RenderFrameKind.Text" /> / <see cref="RenderFrameKind.Raw" />).
    /// </summary>
    public string? Name;

    /// <summary>
    ///     For <see cref="RenderFrameKind.Attribute" />: the attribute value.
    ///     For <see cref="RenderFrameKind.Element" />: the active scoped-CSS id when the
    ///     element opened (or null when no scope is active), so consumers that emit edit-ops
    ///     for inserted elements can re-stamp <c>data-{scopeId}</c> client-side.
    /// </summary>
    public string? Value;

    /// <summary>
    ///     For <see cref="RenderFrameKind.Component" />: the component instance.
    ///     Allows the diff codec to compare by identity, letting cached subtrees
    ///     short-circuit a full frame walk.
    /// </summary>
    public Component? ComponentRef;

    /// <summary>
    ///     For <see cref="RenderFrameKind.Element" />: whether the tag is
    ///     self-closing (<c>&lt;br /&gt;</c>). Persisted on the frame so a consumer
    ///     rendering edit-ops to HTML doesn't need a void-element lookup table.
    /// </summary>
    public bool SelfClosing;

    /// <summary>
    ///     For <see cref="RenderFrameKind.Element" />: whether everything below this element is owned
    ///     by a foreign renderer (see <c>Rask.Islands</c>). <see cref="FrameDiffer" /> compares such an
    ///     element's attributes and then skips its whole subtree by <see cref="SubtreeLength" />,
    ///     because those nodes belong to React/Lit/Blazor and are reconciled on their schedule, not
    ///     ours. Packs into the padding beside <see cref="SelfClosing" />, so the frame does not grow.
    /// </summary>
    public bool Opaque;

    /// <summary>
    ///     UTF-16 character offset into the rendered HTML string at which this
    ///     frame's serialized output begins. Set by <see cref="FrameWriter" /> at
    ///     <c>Open*</c> time; the matching <see cref="HtmlEnd" /> is set at <c>Close*</c>
    ///     time. The diff codec uses <c>[HtmlStart..HtmlEnd]</c> as the HTML fragment to
    ///     ship with an <see cref="RenderFrameKind" />-bearing op (specifically
    ///     <see cref="EditOpKind.InsertSubtree" />) so the client interpreter can apply
    ///     structural changes without re-rendering on its own. Frames without a
    ///     meaningful HTML range (e.g. <see cref="RenderFrameKind.Attribute" />) leave
    ///     these as zero.
    /// </summary>
    public int HtmlStart;

    /// <summary>Companion to <see cref="HtmlStart" />.</summary>
    public int HtmlEnd;
}

/// <summary>
///     Slimmed-down <see cref="RenderFrame" /> for the RETAINED clean-subtree cache (Phase B). Drops
///     the three transient fields a held snapshot never needs — <c>ComponentRef</c> (diff-only) and
///     <c>HtmlStart</c>/<c>HtmlEnd</c> (offsets into one render's HTML, regenerated on replay) — so a
///     mounted page retains ~24 bytes per node instead of the full frame's ~40. The live
///     <see cref="RenderFrame" /> stream that <see cref="FrameDiffer" /> walks is unchanged; only the
///     per-component clean-subtree snapshot uses this leaner shape. On replay,
///     <see cref="HtmlSerializer" /> re-emits the HTML AND writes full frames (with fresh offsets) back
///     into the active <see cref="FrameWriter" /> in one pass.
/// </summary>
public struct LeanFrame
{
    public string? Name;
    public string? Value;
    public int SubtreeLength;
    public RenderFrameKind Kind;
    public bool SelfClosing;

    // Retained with the snapshot rather than recomputed: replay writes frames straight back into the
    // live FrameWriter, so a dropped flag would silently un-protect a cached island's subtree and let
    // the next diff patch into React's DOM.
    public bool Opaque;
}

/// <summary>
///     Writer for a <see cref="RenderFrame" /> stream. Owns a growable
///     <see cref="RenderFrame" /><c>[]</c> rented from <see cref="ArrayPool{T}" /> so
///     steady-state appends amortize to zero allocation across renders. The writer is
///     a regular class (not a <c>ref struct</c>) so <see cref="HtmlSerializer" /> can
///     thread an optional reference through its recursive call chain without ceremony.
/// </summary>
public sealed class FrameWriter : IDisposable
{
    private RenderFrame[] _buffer;

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
    public ReadOnlySpan<RenderFrame> WrittenSpan => _buffer.AsSpan(0, Count);

    /// <summary>Reset the writer for the next render. Re-uses the underlying buffer.</summary>
    public void Reset() => Count = 0;

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

    public void CloseComponent(int openIndex, int htmlEnd)
    {
        _buffer[openIndex].SubtreeLength = Count - openIndex;
        _buffer[openIndex].HtmlEnd = htmlEnd;
    }

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
                prev.Name = (prev.Name ?? string.Empty) + (value ?? string.Empty);
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

        _buffer = [];
        Count = 0;
    }

    private int Reserve()
    {
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

/// <summary>
///     Ambient scope holder for the currently-active <see cref="FrameWriter" />. Lets
///     code paths that don't take the writer as an explicit parameter (notably the
///     <c>WriteAttributes(StringBuilder)</c> overrides on the 48 HTML-element
///     subclasses) emit Attribute frames without a signature change to every subclass.
///     The render walk is single-threaded per session so a <see cref="ThreadStaticAttribute" />
///     is the right shape; the <see cref="Push" /> / <see cref="IDisposable.Dispose" />
///     pattern handles re-entry (error-boundary catch-and-replay, nested ToHtml calls).
/// </summary>
public static class FrameSinkScope
{
    [field: ThreadStatic] public static FrameWriter? Current { get; private set; }

    public static Popper Push(FrameWriter? writer)
    {
        var prev = Current;
        Current = writer;
        return new Popper(prev);
    }

    public readonly struct Popper(FrameWriter? previous) : IDisposable
    {
        public void Dispose() => Current = previous;
    }
}
