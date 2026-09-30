using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;

namespace Rask.Core.Live;

/// <summary>
///     Where <see cref="HtmlSerializer" /> writes: a <see cref="StringBuilder" /> for a page that becomes a string,
///     or a live session's own char buffer (<see cref="RenderedHtmlBuffers" />), so an update is neither regrown in
///     a pooled builder nor copied out of one (#1141).
/// </summary>
internal sealed class HtmlWriter
{
    // An attribute scratch that grew past this is dropped rather than kept by the thread.
    private const int MaxRetainedAttributeChars = 4 * 1024;

    // Writers nest — a ToHtml() inside a page's render — so one is kept per depth, per thread.
    [ThreadStatic] private static HtmlWriter?[]? _spares;
    [ThreadStatic] private static int _depth;
    [ThreadStatic] private static StringBuilder? _attributes;

    private StringBuilder? _sb;
    private char[] _chars = [];
    private int _length;

    /// <summary>The chars written so far — what frame offsets are measured in. Set it lower to rewind.</summary>
    public int Length
    {
        get => _sb?.Length ?? _length;
        set
        {
            if (_sb is { } sb)
            {
                sb.Length = value;
            }
            else
            {
                _length = value;
            }
        }
    }

    /// <summary>A writer appending to <paramref name="sb" />; hand it back with <see cref="Release" />.</summary>
    public static HtmlWriter Over(StringBuilder sb)
    {
        var writer = Take();
        writer._sb = sb;
        return writer;
    }

    /// <summary>A writer filling <paramref name="buffer" /> from its start, growing it through the shared pool.</summary>
    public static HtmlWriter Into(char[] buffer)
    {
        var writer = Take();
        writer._chars = buffer;
        writer._length = 0;
        return writer;
    }

    /// <summary>Returns a writer from <see cref="Over" /> or <see cref="Into" /> for the next call on this thread.</summary>
    public void Release()
    {
        _sb = null;
        Unwind();
    }

    /// <summary>Hands back the buffer — regrown, perhaps — and how much of it was written.</summary>
    public char[] Detach(out int length)
    {
        var buffer = _chars;
        length = _length;
        _chars = [];
        _length = 0;
        return buffer;
    }

    public HtmlWriter Append(char c)
    {
        if (_sb is { } sb)
        {
            sb.Append(c);
            return this;
        }

        if (_length == _chars.Length)
        {
            Grow(1);
        }

        _chars[_length++] = c;
        return this;
    }

    public HtmlWriter Append(string? s) => Append(s.AsSpan());

    public HtmlWriter Append(ReadOnlySpan<char> s)
    {
        if (_sb is { } sb)
        {
            sb.Append(s);
            return this;
        }

        if (_chars.Length - _length < s.Length)
        {
            Grow(s.Length);
        }

        s.CopyTo(_chars.AsSpan(_length));
        _length += s.Length;
        return this;
    }

    /// <summary>Appends <paramref name="value" /> HTML-encoded, exactly as <see cref="HtmlEncoder.Default" /> would.</summary>
    public void AppendEncoded(ReadOnlySpan<char> value)
    {
        if (_sb is { } sb)
        {
            HtmlSerializer.AppendEncoded(sb, value);
            return;
        }

        var unsafeAt = value.IndexOfAnyExcept(HtmlSerializer.SafeAsciiForHtml);
        if (unsafeAt < 0)
        {
            Append(value);
            return;
        }

        // Encoded straight into the buffer: no stack chunk to copy out of.
        Append(value[..unsafeAt]);
        var rest = value[unsafeAt..];
        while (true)
        {
            var status = HtmlEncoder.Default.Encode(rest, _chars.AsSpan(_length), out var consumed, out var written);
            _length += written;
            if (status != OperationStatus.DestinationTooSmall)
            {
                return;
            }

            rest = rest[consumed..];
            Grow(rest.Length + 16);
        }
    }

    /// <summary>Writes <paramref name="element" />'s attributes; the public hook writes to a builder, so a buffer takes them through a small scratch one.</summary>
    public void WriteAttributes(Component element)
    {
        if (_sb is { } sb)
        {
            element.WriteAttributesInternal(sb);
            return;
        }

        var scratch = TakeScratch();
        element.WriteAttributesInternal(scratch);
        foreach (var chunk in scratch.GetChunks())
        {
            Append(chunk.Span);
        }

        ReturnScratch(scratch);
    }

    /// <summary>Replaces <paramref name="removeLength" /> chars at <paramref name="index" /> with <paramref name="insert" />, shifting the tail.</summary>
    public void Replace(int index, int removeLength, StringBuilder insert)
    {
        if (_sb is { } sb)
        {
            sb.Remove(index, removeLength);
            if (insert.Length > 0)
            {
                // No Insert(int, StringBuilder); the block is a handful of <head> tags.
                sb.Insert(index, insert.ToString());
            }

            return;
        }

        var delta = insert.Length - removeLength;
        if (_chars.Length - _length < delta)
        {
            Grow(delta);
        }

        var tailStart = index + removeLength;
        _chars.AsSpan(tailStart, _length - tailStart).CopyTo(_chars.AsSpan(index + insert.Length));
        insert.CopyTo(0, _chars.AsSpan(index), insert.Length);
        _length += delta;
    }

    private static HtmlWriter Take()
    {
        var spares = _spares ??= new HtmlWriter?[4];
        var writer = _depth < spares.Length ? spares[_depth] ??= new HtmlWriter() : new HtmlWriter();
        _depth++;
        return writer;
    }

    private static void Unwind() => _depth--;

    // Taken rather than shared, so a nested render on this thread gets its own.

    private static StringBuilder TakeScratch()
    {
        var scratch = _attributes ?? new StringBuilder();
        _attributes = null;
        return scratch;
    }

    private static void ReturnScratch(StringBuilder scratch)
    {
        if (scratch.Capacity <= MaxRetainedAttributeChars)
        {
            _attributes = scratch.Clear();
        }
    }

    private void Grow(int more)
    {
        var next = ArrayPool<char>.Shared.Rent(Math.Max(_length + more, _chars.Length * 2));
        _chars.AsSpan(0, _length).CopyTo(next);

        // The empty array a fresh buffer starts as never came from the pool.
        if (_chars.Length > 0)
        {
            ArrayPool<char>.Shared.Return(_chars);
        }

        _chars = next;
    }
}
