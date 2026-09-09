namespace Rask.Ui;

/// <summary>
/// An image cropped to a shape.
/// </summary>
/// <remarks>
/// The shape is daisyUI's own class — <c>mask-squircle</c>, <c>mask-hexagon</c>, <c>mask-star</c> — passed
/// through rather than enumerated, because the set is long, purely decorative, and grows without the kit
/// having anything to say about it.
/// </remarks>
public sealed partial class UiMask : Div
{

    /// <summary>The shape it is clipped to.</summary>
    public required UiMaskShape Shape { get; set; }

    /// <inheritdoc />
    protected override string? ResolveClass() =>
        UiClass.Compose("mask", UiClassNames.MaskShape(Shape), Class);
}
