using System.Buffers;

namespace Rask.Core.Live;

/// <summary>
///     Tag distinguishing the role of a frame in a render-tree stream. The stream is
///     emitted by <see cref="HtmlSerializer.Serialize(Component, System.Text.StringBuilder, FrameWriter?)" />
///     alongside the rendered HTML and consumed by <see cref="FrameDiffer" /> when
///     comparing successive renders to emit a minimal edit-op payload.
/// </summary>
public enum RenderFrameKind : byte
{
    /// <summary>Opens an HTML element. The matching closing tag is implicit at
    /// <c>index + SubtreeLength</c>; there is no <c>CloseElement</c> frame.</summary>
    Element = 1,

    /// <summary>A single name/value attribute on the most-recently-opened element.</summary>
    Attribute = 2,

    /// <summary>HTML-encoded text content (the producer is responsible for any encoding
    /// decisions — the frame stores the raw user-supplied text so a consumer can
    /// re-encode if it writes to a different sink).</summary>
    Text = 3,

    /// <summary>Verbatim markup (no encoding). Corresponds to <see cref="Generated.Raw" />.</summary>
    Raw = 4,

    /// <summary>Doctype declaration.</summary>
    Doctype = 5,

    /// <summary>Marks the start of a user-component's rendered subtree. The component
    /// instance reference lets the diff codec short-circuit when an unchanged component
    /// instance still produces an identical cached subtree.</summary>
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

    /// <summary>For <see cref="RenderFrameKind.Element" /> and
    /// <see cref="RenderFrameKind.Component" />: total frames in the subtree rooted at
    /// this frame including itself. <c>1</c> means a leaf element with no children. The
    /// field is patched in by <see cref="FrameWriter.CloseElement" /> /
    /// <see cref="FrameWriter.CloseComponent" /> at close time; while the element is
    /// still open the value is meaningless.</summary>
    public int SubtreeLength;

    /// <summary>
    ///     Element tag (for <see cref="RenderFrameKind.Element" />), attribute name
    ///     (for <see cref="RenderFrameKind.Attribute" />), or text content (for
    ///     <see cref="RenderFrameKind.Text" /> / <see cref="RenderFrameKind.Raw" />).
    /// </summary>
    public string? Name;

    /// <summary>For <see cref="RenderFrameKind.Attribute" />: the attribute value.
    /// For <see cref="RenderFrameKind.Element" />: the active scoped-CSS id when the
    /// element opened (or null when no scope is active), so consumers that emit edit-ops
    /// for inserted elements can re-stamp <c>data-{scopeId}</c> client-side.</summary>
    public string? Value;

    /// <summary>For <see cref="RenderFrameKind.Component" />: the component instance.
    /// Allows the diff codec to compare by identity, letting cached subtrees
    /// short-circuit a full frame walk.</summary>
    public Component? ComponentRef;

    /// <summary>For <see cref="RenderFrameKind.Element" />: whether the tag is
    /// self-closing (<c>&lt;br /&gt;</c>). Persisted on the frame so a consumer
    /// rendering edit-ops to HTML doesn't need a void-element lookup table.</summary>
    public bool SelfClosing;

    /// <summary>UTF-16 character offset into the rendered HTML string at which this
    /// frame's serialized output begins. Set by <see cref="FrameWriter" /> at
    /// <c>Open*</c> time; the matching <see cref="HtmlEnd" /> is set at <c>Close*</c>
    /// time. The diff codec uses <c>[HtmlStart..HtmlEnd]</c> as the HTML fragment to
    /// ship with an <see cref="RenderFrameKind"/>-bearing op (specifically
    /// <see cref="EditOpKind.InsertSubtree" />) so the client interpreter can apply
    /// structural changes without re-rendering on its own. Frames without a
    /// meaningful HTML range (e.g. <see cref="RenderFrameKind.Attribute" />) leave
    /// these as zero.</summary>
    public int HtmlStart;

    /// <summary>Companion to <see cref="HtmlStart" />.</summary>
    public int HtmlEnd;
}

/// <summary>
///     Writer for a <see cref="RenderFrame" /> stream. Owns a growable
///     <see cref="RenderFrame" /><c>[]</c> rented from <see cref="ArrayPool{T}" /> so
///     steady-state appends amortize to zero allocation across renders. The writer is
///     a regular class (not a <c>ref struct</c>) so <see cref="HtmlSerializer" /> can
///     thread an optional reference through its recursive call chain without ceremony.
/// </summary>
public sealed class FrameWriter
{
    private RenderFrame[] _buffer;
    private int _count;

    public FrameWriter(int initialCapacity = 256)
    {
        _buffer = ArrayPool<RenderFrame>.Shared.Rent(Math.Max(16, initialCapacity));
    }

    /// <summary>Total frames emitted so far.</summary>
    public int Count => _count;

    /// <summary>View over the emitted frames. Stable for the duration of one render
    /// — invalidated by the next <c>Open*</c>/<c>Reset</c> call that triggers a resize.</summary>
    public ReadOnlySpan<RenderFrame> WrittenSpan => _buffer.AsSpan(0, _count);

    /// <summary>Reset the writer for the next render. Re-uses the underlying buffer.</summary>
    public void Reset() => _count = 0;

    /// <summary>Open a regular HTML element. Returns the frame index so the caller
    /// can pass it back to <see cref="CloseElement" /> to patch in the subtree length
    /// and the HTML byte range.</summary>
    public int OpenElement(string tag, string? scopeId, bool selfClosing, int htmlStart)
    {
        var idx = Reserve();
        _buffer[idx] = new RenderFrame
        {
            Kind = RenderFrameKind.Element,
            Name = tag,
            Value = scopeId,
            SelfClosing = selfClosing,
            SubtreeLength = 1,
            HtmlStart = htmlStart
        };
        return idx;
    }

    public void CloseElement(int openIndex, int htmlEnd)
    {
        _buffer[openIndex].SubtreeLength = _count - openIndex;
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
        _buffer[openIndex].SubtreeLength = _count - openIndex;
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

    private int Reserve()
    {
        if (_count == _buffer.Length)
        {
            var bigger = ArrayPool<RenderFrame>.Shared.Rent(_buffer.Length * 2);
            Array.Copy(_buffer, bigger, _count);
            ArrayPool<RenderFrame>.Shared.Return(_buffer, clearArray: true);
            _buffer = bigger;
        }

        return _count++;
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
    [ThreadStatic] private static FrameWriter? _current;

    public static FrameWriter? Current => _current;

    public static Popper Push(FrameWriter? writer)
    {
        var prev = _current;
        _current = writer;
        return new Popper(prev);
    }

    public readonly struct Popper(FrameWriter? previous) : IDisposable
    {
        public void Dispose() => _current = previous;
    }
}
