using Rask.Native.Components;

namespace Rask.Native.Surface;

/// <summary>
///     The three props that together decide a button's fill and title colour — <c>Style</c>,
///     <c>Background</c> and <c>Color</c> — kept as one piece of state so the painted result does not
///     depend on the order they arrive in.
/// </summary>
/// <remarks>
///     <para>
///         A backend that paints each prop as it arrives gets the wrong answer the moment <c>Style</c>
///         lands last: a style carries its own fill and title colour, so applying it discards the
///         explicit colours applied before it. Both backends did exactly that (#785) — iOS by assigning
///         a fresh <c>UIButtonConfiguration</c>, Android by calling <c>SetBackgroundColor</c> /
///         <c>SetTextColor</c> unconditionally — which made two identical component trees paint
///         differently for no reason the component surface hints at.
///     </para>
///     <para>
///         <c>NativeButton</c> already documents the rule: an explicit <c>Background</c> / <c>Color</c>
///         wins, and <see langword="null" /> lets <c>Style</c> decide. Holding all three and re-deriving
///         the pair on every write is what makes that rule true whatever order the patch applies them
///         in, and keeping it here means it is stated once rather than twice in platform code no test
///         on a build machine can reach.
///     </para>
///     <para>
///         A class rather than a struct on purpose: the backends hold this on the retained view and
///         mutate it in place, and a mutable struct behind a property would take the write on a copy
///         and silently lose it — the same class of bug one level down.
///     </para>
/// </remarks>
public sealed class NativeButtonAppearance
{
    /// <summary>The visual treatment. <see cref="NativeButtonStyle.Filled" /> until a patch says otherwise.</summary>
    public NativeButtonStyle Style { get; private set; } = NativeButtonStyle.Filled;

    /// <summary>The explicit fill colour, or <see langword="null" /> to let <see cref="Style" /> decide.</summary>
    public string? Background { get; private set; }

    /// <summary>The explicit title colour, or <see langword="null" /> to let <see cref="Style" /> decide.</summary>
    public string? Foreground { get; private set; }

    /// <summary>Records one prop write, if it is one of the three that decide the appearance.</summary>
    /// <param name="id">The prop being written.</param>
    /// <param name="value">Its value; ignored when <paramref name="unset" /> is <see langword="true" />.</param>
    /// <param name="unset">Whether the prop is being cleared rather than set.</param>
    /// <returns>
    ///     <see langword="true" /> when the appearance changed and the caller must repaint the whole of
    ///     it — never just the prop that arrived.
    /// </returns>
    public bool Write(NativePropId id, NativePropValue value, bool unset)
    {
        switch (id)
        {
            case NativePropId.Style:
                Style = unset ? NativeButtonStyle.Filled : (NativeButtonStyle)(int)value.Number;
                return true;

            case NativePropId.Background:
                Background = unset ? null : value.Text;
                return true;

            case NativePropId.Color:
                Foreground = unset ? null : value.Text;
                return true;

            default:
                return false;
        }
    }
}
