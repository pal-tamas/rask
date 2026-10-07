namespace Rask;

/// <summary>
/// Media that runs out to the card's edges instead of sitting inside the padding. Flux UI's <c>card.bleed</c>.
/// </summary>
/// <remarks>
/// It always reaches the sides, reaches the top or bottom when it is the first or last thing in the body, and
/// rounds only the corners it touches. How far that is comes from <c>--ui-bleed-x</c>, <c>--ui-bleed-top</c>,
/// <c>--ui-bleed-bottom</c> and the two <c>--ui-bleed-*-radius</c> variables, which the card and its body set
/// (Flux's <c>--flux-bleed-*</c>); the extra pixel is the edge's own border, which media covers.
/// </remarks>
public sealed partial class UiCardBleed : Component
{
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class(UiClass.Compose(
                "-mx-[calc(var(--ui-bleed-x)+1px)] overflow-hidden",
                "first:-mt-[calc(var(--ui-bleed-top)+1px)] first:rounded-t-[calc(var(--ui-bleed-top-radius)+1px)]",
                "last:-mb-[calc(var(--ui-bleed-bottom)+1px)] last:rounded-b-[calc(var(--ui-bleed-bottom-radius)+1px)]",
                Class))
            .Data("ui-card-bleed")[Children ?? []];
}
