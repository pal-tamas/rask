namespace Rask;

/// <summary>
/// A container for related content, such as a form, a list or a summary. Flux UI's card.
/// </summary>
/// <remarks>
/// <para>
/// Give it a <see cref="UiCardHeader" />, a <see cref="UiCardBody" /> and a <see cref="UiCardFooter" /> and it
/// handles the spacing, dividers and corners between them; <see cref="Body" /> decides how they are set apart.
/// The parts are optional: anything put straight inside is padded evenly, like a bordered <c>&lt;div&gt;</c>.
/// </para>
/// <code>
/// Ui.Card.Inset.Soft[
///     Ui.CardHeader[Ui.CardHeading["Profile"], Ui.CardActions[Ui.Button["Edit"]]],
///     Ui.CardBody[form],
///     Ui.CardFooter[Ui.CardActions[Ui.Button["Save"]]]
/// ]
/// </code>
/// </remarks>
public sealed partial class UiCard : Component
{
    /// <summary>How the header, body and footer are set apart. Seamless unless this says otherwise.</summary>
    /// <remarks>Hides the inherited <c>Body</c> tag entry inside this component; <c>Markup.Body</c> still reaches the tag.</remarks>
    public new Ui.CardBodyVariant? Body { get; set; }

    /// <summary>The card's surface.</summary>
    public Ui.CardVariant? Variant { get; set; }

    /// <summary>Scales the card's padding, corners and spacing.</summary>
    public Ui.CardSize? Size { get; set; }

    /// <summary>With <see cref="Ui.CardBodyVariant.Divided" />, where the lines stop.</summary>
    public Ui.CardDivider? Divider { get; set; }

    /// <summary>A faint highlight along the inside of the card's top edge, in light mode. On unless this is <c>false</c>.</summary>
    public bool? Highlight { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var scope = new UiCardScope(
            Size ?? Ui.CardSize.Md,
            Body ?? Ui.CardBodyVariant.Seamless,
            Variant ?? Ui.CardVariant.Default,
            Divider ?? Ui.CardDivider.Full);

        var filled = scope.Variant == Ui.CardVariant.Filled;
        var classes = UiClass.Compose(
            // Filled has no edge to draw a highlight inside, so it is not a positioning context either.
            filled ? "border" : "relative border bg-clip-padding",
            Radius(scope.Size),
            Padding(scope),
            Surface(scope.Variant),
            filled || Highlight == false ? "" : EdgeHighlight,
            Bleed(scope),
            Class);

        return Div.Data(Markers(scope)).Class(classes)[Context.Provide(scope)[Children ?? []]];
    }

    // Flux's markers, under the kit's prefix, and as Flux writes them: the body and the surface always, the
    // size and an inset line only when the call site said so.
    private Dictionary<string, string?> Markers(UiCardScope scope)
    {
        var markers = new Dictionary<string, string?>(StringComparer.Ordinal) { ["ui-card"] = null };
        if (Size is { } size)
        {
            markers["ui-card-size"] = Name(size);
        }

        markers["ui-card-body-variant"] = Name(scope.Body);
        markers["ui-card-variant"] = Name(scope.Variant);
        if (scope.Divider == Ui.CardDivider.Inset)
        {
            markers["ui-card-divider"] = "inset";
        }

        return markers;
    }

    // The ring is white, so it only shows over a tint or a shadow, and it fades out towards the bottom edge.
    private const string EdgeHighlight =
        "after:pointer-events-none after:absolute after:inset-0 after:rounded-[calc(var(--ui-card-radius)-1px)] "
        + "after:inset-ring after:inset-ring-white/25 after:[mask-image:linear-gradient(black,transparent)] dark:after:hidden";

    private static string Radius(Ui.CardSize size) => size switch
    {
        Ui.CardSize.Lg => "rounded-2xl [--ui-card-radius:var(--radius-2xl)]",
        Ui.CardSize.Md => "rounded-xl [--ui-card-radius:var(--radius-xl)]",
        _ => "rounded-lg [--ui-card-radius:var(--radius-lg)]",
    };

    // Seamless pads everything evenly; inset leaves a hairline of card around the panel; the rest pad per part.
    private static string Padding(UiCardScope scope) => scope.Body switch
    {
        Ui.CardBodyVariant.Seamless => scope.Roomy ? "p-6" : "p-4",
        Ui.CardBodyVariant.Inset => "p-1",
        _ => "",
    };

    private static string Surface(Ui.CardVariant variant) => variant switch
    {
        Ui.CardVariant.Muted => "border-zinc-900/10 bg-zinc-900/4 dark:border-white/12 dark:bg-white/7",
        Ui.CardVariant.Soft => "border-zinc-900/7 bg-zinc-900/2 dark:border-white/7 dark:bg-white/5",
        Ui.CardVariant.Outline => "border-zinc-900/10 dark:border-white/15",
        Ui.CardVariant.Filled => "border-transparent bg-zinc-900/3 dark:bg-white/6",
        _ => "border-zinc-900/10 bg-white shadow-xs dark:border-white/10 dark:bg-white/10 dark:shadow-none",
    };

    // Flux's --flux-bleed-*: how far bleeding content (a Ui.CardBleed, a bleeding table) travels to reach the
    // nearest visible edge, and the corner it then has to follow. A body restates them for what is inside it.
    private static string Bleed(UiCardScope scope) => UiClass.Compose(
        scope.Body switch
        {
            Ui.CardBodyVariant.Seamless => scope.Roomy ? "[--ui-bleed-x:--spacing(6)]" : "[--ui-bleed-x:--spacing(4)]",
            Ui.CardBodyVariant.Inset => "[--ui-bleed-x:--spacing(1)]",
            _ => "[--ui-bleed-x:0px]",
        },
        "[--ui-bleed:var(--ui-bleed-x)] [--ui-bleed-top:var(--ui-bleed-x)] [--ui-bleed-bottom:var(--ui-bleed-x)]",
        "[--ui-bleed-top-radius:calc(var(--ui-card-radius)-1px)] [--ui-bleed-bottom-radius:calc(var(--ui-card-radius)-1px)]");

    private static string Name(Ui.CardSize size) => size switch
    {
        Ui.CardSize.Xs => "xs",
        Ui.CardSize.Sm => "sm",
        Ui.CardSize.Lg => "lg",
        _ => "md",
    };

    private static string Name(Ui.CardBodyVariant? body) => body switch
    {
        Ui.CardBodyVariant.Inset => "inset",
        Ui.CardBodyVariant.Flush => "flush",
        Ui.CardBodyVariant.Divided => "divided",
        Ui.CardBodyVariant.Separated => "separated",
        _ => "seamless",
    };

    private static string Name(Ui.CardVariant variant) => variant switch
    {
        Ui.CardVariant.Muted => "muted",
        Ui.CardVariant.Soft => "soft",
        Ui.CardVariant.Outline => "outline",
        Ui.CardVariant.Filled => "filled",
        _ => "default",
    };
}
