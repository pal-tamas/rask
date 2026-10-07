namespace Rask;

/// <summary>What a card tells the parts inside it, so each can space itself to match.</summary>
/// <param name="Size">The card's size — or, outside a card, the header's or footer's own.</param>
/// <param name="Body">The card's body treatment; <c>null</c> outside a card.</param>
/// <param name="Variant">The card's surface.</param>
/// <param name="Divider">Where a divided card's lines stop.</param>
/// <param name="Bare">Inside a body, where a header or footer is a sub-section with no treatment.</param>
/// <param name="Footer">Inside a footer, where actions centre on the row instead of tucking into the corner.</param>
internal sealed record UiCardScope(
    Ui.CardSize Size,
    Ui.CardBodyVariant? Body,
    Ui.CardVariant Variant = Ui.CardVariant.Default,
    Ui.CardDivider Divider = Ui.CardDivider.Full,
    bool Bare = false,
    bool Footer = false)
{
    /// <summary>What a header or footer outside any card is spaced for.</summary>
    internal static UiCardScope Standalone(Ui.CardSize? size) => new(size ?? Ui.CardSize.Md, null);

    /// <summary>Md and Lg share their padding and line height; Xs and Sm are the tight pair.</summary>
    internal bool Roomy => Size is Ui.CardSize.Md or Ui.CardSize.Lg;

    /// <summary>The treatment a header or footer draws: none inside a body, the card's otherwise.</summary>
    internal Ui.CardBodyVariant? Edge => Bare ? Ui.CardBodyVariant.Seamless : Body;

    /// <summary>A tinted card's panel is raised off it; a white card's is a recessed well.</summary>
    internal bool Tinted => Variant is Ui.CardVariant.Muted or Ui.CardVariant.Soft or Ui.CardVariant.Filled;
}
