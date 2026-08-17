namespace Rask.Bootstrap;

// Bootstrap card and its sections. Compose them as nested children, e.g.
//   BsCard()[ BsCardImage(Src: "…"), BsCardBody()[ BsCardTitle()["Title"], BsCardText()["…"] ] ]

/// <summary>
///     A bordered content container — the general-purpose Bootstrap surface. Compose it from the header,
///     body, footer, title and image parts rather than nesting raw markup.
/// </summary>
public sealed partial class BsCard : BsBlock
{
    // Fills the whole card with a theme color via the contrast-aware text-bg-* helper.

    /// <summary>The semantic colour of the card's border and background.</summary>
    public BsColor? Color { get; set; }

    protected override Component? Render() =>
        Div.Id(Id).Class(BsClass.Join("card", Color is { } c ? c.TextBg() : null, Class))[Items];
}

/// <summary>
///     The card's header strip, above the body.
/// </summary>
public sealed partial class BsCardHeader : BsBlock
{
    protected override Component? Render() => Wrap("card-header");
}

/// <summary>
///     The card's padded main content. Almost every card wants one — it supplies the padding the other
///     parts assume.
/// </summary>
public sealed partial class BsCardBody : BsBlock
{
    protected override Component? Render() => Wrap("card-body");
}

/// <summary>
///     The card's footer strip, below the body.
/// </summary>
public sealed partial class BsCardFooter : BsBlock
{
    protected override Component? Render() => Wrap("card-footer");
}

/// <summary>
///     The card's heading. Give it a real heading level with the underlying element so the page outline
///     stays intact.
/// </summary>
public sealed partial class BsCardTitle : BsBlock
{
    protected override Component? Render() =>
        H5.Id(Id).Class(BsClass.Join("card-title", Class))[Items];
}

/// <summary>
///     A muted subheading beneath the title.
/// </summary>
public sealed partial class BsCardSubtitle : BsBlock
{
    protected override Component? Render() =>
        H6.Id(Id).Class(BsClass.Join("card-subtitle", "mb-2", "text-body-secondary", Class))[Items];
}

/// <summary>
///     A paragraph of card text.
/// </summary>
public sealed partial class BsCardText : BsBlock
{
    protected override Component? Render() =>
        P.Id(Id).Class(BsClass.Join("card-text", Class))[Items];
}

// A card image. Renders <img class="card-img-top"> by default, or card-img-bottom when Bottom is set.

/// <summary>
///     An image capping the top or bottom of a card. <c>Alt</c> is required — a decorative image should
///     pass an empty string.
/// </summary>
public sealed partial class BsCardImage : BsBlock
{
    /// <summary>The image's URL.</summary>
    public string? Src { get; set; }

    /// <summary>
    ///     The text that replaces the image for anyone who cannot see it. Empty for a decorative image.
    /// </summary>
    public string? Alt { get; set; }

    /// <summary>Caps the bottom of the card rather than the top.</summary>
    public bool? Bottom { get; set; }

    protected override Component? Render() =>
        Img
            .Id(Id)
            .Class(BsClass.Join(Bottom is true ? "card-img-bottom" : "card-img-top", Class))
            .Src(Src)
            .Alt(Alt);
}
