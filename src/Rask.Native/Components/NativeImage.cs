using Rask.Native.Surface;

namespace Rask.Native.Components;

/// <summary>
///     An image, projected to a <c>UIImageView</c> (iOS) or an <c>ImageView</c> (Android).
/// </summary>
/// <remarks>
///     <see cref="Source" /> is either a bundled asset name (an asset-catalog entry on iOS, a drawable on
///     Android) or an absolute URL the backend fetches. Prefer a bundled asset: it paints on the first frame,
///     needs no network, and works offline.
/// </remarks>
public sealed partial class NativeImage : NativeViewComponent
{
    /// <summary>A bundled asset name or an absolute URL. Required.</summary>
    /// <remarks>
    ///     Shadows the generated <c>Source</c> markup entry, which a native component has no use for — it
    ///     renders platform views, never HTML.
    /// </remarks>
    public required string Source { get; set; }

    /// <summary>How the image scales into its frame. Leave <c>null</c> for <see cref="NativeContentMode.Fit" />.</summary>
    public NativeContentMode? ContentMode { get; set; }

    /// <summary>A fixed width in points. Leave <c>null</c> to size to content.</summary>
    public double? Width { get; set; }

    /// <summary>A fixed height in points. Leave <c>null</c> to size to content.</summary>
    public double? Height { get; set; }

    /// <summary>An accessibility label describing the image, for screen readers.</summary>
    public string? AccessibilityId { get; set; }

    /// <inheritdoc />
    internal override NativeNodeKind SurfaceKind => NativeNodeKind.Image;

    /// <inheritdoc />
    internal override void WriteSurfaceProps(ref NativePropWriter props)
    {
        props.Text(NativePropId.Source, Source);
        props.Enum(NativePropId.ContentMode, ContentMode);
        props.Number(NativePropId.Width, Width);
        props.Number(NativePropId.Height, Height);
        props.Text(NativePropId.AccessibilityId, AccessibilityId);
    }
}
