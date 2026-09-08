namespace Rask.Ui;

/// <summary>
/// Several images in the space of one, each revealed by hovering its column.
/// </summary>
/// <remarks>
/// <para>
/// The first child is what shows at rest and the rest are stacked behind it, so put the image that has
/// to work on its own first: on a touch screen, that is the only one anybody sees. daisyUI lays out up
/// to nine and ignores the rest.
/// </para>
/// <para>
/// Its children are stretched to the container's height, so the container needs one — give it a height
/// or an aspect ratio through <see cref="Class" />, or it collapses to nothing.
/// </para>
/// </remarks>
public sealed partial class UiHoverGallery : Component
{
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        // <figure>, which is what a run of images with no individual caption is. daisyUI styles the
        // class on any element and specifically handles the figure case.
        Figure.Class(UiClass.Compose("hover-gallery", Class))[Children ?? []];
}
