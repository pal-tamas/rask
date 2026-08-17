using Rask.Native.Surface;

namespace Rask.Native.Components;

/// <summary>
///     Empty space inside a <see cref="NativeStack" />. With no size it expands to absorb whatever room is
///     left, which is how you push siblings to opposite ends of a row; give it a
///     <see cref="Width" />/<see cref="Height" /> instead for a fixed gap.
/// </summary>
/// <example>
///     <code>NativeStack(Orientation: NativeOrientation.Horizontal)[
///         NativeLabel(Text: "Total"), NativeSpacer(), NativeLabel(Text: "$42.00")]</code>
/// </example>
public sealed partial class NativeSpacer : NativeViewComponent
{
    /// <summary>A fixed width in points. Leave <c>null</c> to expand along a horizontal stack.</summary>
    public double? Width { get; set; }

    /// <summary>A fixed height in points. Leave <c>null</c> to expand along a vertical stack.</summary>
    public double? Height { get; set; }

    /// <inheritdoc />
    internal override NativeNodeKind SurfaceKind => NativeNodeKind.Spacer;

    /// <inheritdoc />
    internal override void WriteSurfaceProps(ref NativePropWriter props)
    {
        props.Number(NativePropId.Width, Width);
        props.Number(NativePropId.Height, Height);
    }
}
