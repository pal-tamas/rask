using Rask.Native.Surface;

namespace Rask.Native.Components;

/// <summary>
///     A scrolling viewport around its children, projected to a <c>UIScrollView</c> (iOS) or a
///     <c>NestedScrollView</c> (Android). Wrap a tall <see cref="NativeStack" /> in one to make a form or an
///     article scroll; for a list of rows that come and go, <see cref="NativeList" /> reconciles them by key.
/// </summary>
public sealed partial class NativeScroll : NativeViewComponent
{
    /// <summary>Uniform inner padding in points. Leave <c>null</c> for none.</summary>
    public double? Padding { get; set; }

    /// <summary>The background color. Leave <c>null</c> for transparent.</summary>
    public NativeColor? Background { get; set; }

    /// <inheritdoc />
    internal override NativeNodeKind SurfaceKind => NativeNodeKind.Scroll;

    /// <inheritdoc />
    internal override bool AcceptsChildren => true;

    /// <inheritdoc />
    internal override void WriteSurfaceProps(ref NativePropWriter props)
    {
        props.Number(NativePropId.Padding, Padding);
        props.Color(NativePropId.Background, Background);
    }
}
