using Rask.Native.Surface;

namespace Rask.Native.Components;

/// <summary>
///     A hairline separator — one physical pixel on both platforms, so it stays crisp on every screen density.
///     The pure-native counterpart of <c>Hr</c>.
/// </summary>
public sealed partial class NativeDivider : NativeViewComponent
{
    /// <summary>The rule's color. Leave <c>null</c> for the platform's separator color.</summary>
    public NativeColor? Color { get; set; }

    /// <inheritdoc />
    internal override NativeNodeKind SurfaceKind => NativeNodeKind.Divider;

    /// <inheritdoc />
    internal override void WriteSurfaceProps(ref NativePropWriter props) =>
        props.Color(NativePropId.Color, Color);
}
