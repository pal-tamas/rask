using Rask.Native.Surface;

namespace Rask.Native.Components;

/// <summary>
///     A spinning progress indicator, projected to a <c>UIActivityIndicatorView</c> (iOS) or a
///     <c>ProgressBar</c> (Android). Render one while an <c>await</c> is in flight.
/// </summary>
public sealed partial class NativeActivityIndicator : NativeViewComponent
{
    /// <summary>Whether it is spinning. Leave <c>null</c> to spin — an indicator that is rendered is normally busy.</summary>
    public bool? Animating { get; set; }

    /// <summary>The spinner's color. Leave <c>null</c> for the platform default.</summary>
    public NativeColor? Color { get; set; }

    /// <inheritdoc />
    internal override NativeNodeKind SurfaceKind => NativeNodeKind.ActivityIndicator;

    /// <inheritdoc />
    internal override void WriteSurfaceProps(ref NativePropWriter props)
    {
        props.Flag(NativePropId.Animating, Animating);
        props.Color(NativePropId.Color, Color);
    }
}
