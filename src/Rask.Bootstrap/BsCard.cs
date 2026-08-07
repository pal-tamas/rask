namespace Rask.Bootstrap;

// Bootstrap card and its sections. Compose them as nested children, e.g.
//   BsCard()[ BsCardImage(Src: "…"), BsCardBody()[ BsCardTitle()["Title"], BsCardText()["…"] ] ]
public sealed partial class BsCard : BsBlock
{
    // Fills the whole card with a theme color via the contrast-aware text-bg-* helper.
    public BsColor? Color { get; set; }

    protected override Component? Render() =>
        Div(Id: Id, Class: BsClass.Join("card", Color is { } c ? c.TextBg() : null, Class))[Items];
}

public sealed partial class BsCardHeader : BsBlock
{
    protected override Component? Render() => Wrap("card-header");
}

public sealed partial class BsCardBody : BsBlock
{
    protected override Component? Render() => Wrap("card-body");
}

public sealed partial class BsCardFooter : BsBlock
{
    protected override Component? Render() => Wrap("card-footer");
}

public sealed partial class BsCardTitle : BsBlock
{
    protected override Component? Render() =>
        H5(Id: Id, Class: BsClass.Join("card-title", Class))[Items];
}

public sealed partial class BsCardSubtitle : BsBlock
{
    protected override Component? Render() =>
        H6(Id: Id, Class: BsClass.Join("card-subtitle", "mb-2", "text-body-secondary", Class))[Items];
}

public sealed partial class BsCardText : BsBlock
{
    protected override Component? Render() =>
        P(Id: Id, Class: BsClass.Join("card-text", Class))[Items];
}

// A card image. Renders <img class="card-img-top"> by default, or card-img-bottom when Bottom is set.
public sealed partial class BsCardImage : BsBlock
{
    public string? Src { get; set; }
    public string? Alt { get; set; }
    public bool? Bottom { get; set; }

    protected override Component? Render() =>
        Img(Id: Id, Class: BsClass.Join(Bottom is true ? "card-img-bottom" : "card-img-top", Class),
            Src: Src, Alt: Alt);
}
