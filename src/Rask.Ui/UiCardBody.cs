namespace Rask;

/// <summary>
/// A card's main content. Flux UI's <c>card.body</c>.
/// </summary>
/// <remarks>
/// How it is drawn is the card's <see cref="UiCard.Body" />: nothing at all, a panel of its own, or padded
/// between the header's and footer's lines or bands. It can hold a <see cref="UiCardHeader" /> and
/// <see cref="UiCardFooter" /> of its own, which then title a sub-section of it.
/// </remarks>
public sealed partial class UiCardBody : Component
{
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var scope = Context.Get<UiCardScope>() ?? UiCardScope.Standalone(null);
        var classes = UiClass.Compose(
            scope.Body switch
            {
                Ui.CardBodyVariant.Inset => Inset(scope),
                Ui.CardBodyVariant.Flush => Flush(scope),
                Ui.CardBodyVariant.Divided or Ui.CardBodyVariant.Separated => Padded(scope),
                _ => Seamless(scope),
            },
            Class);

        return Div.Class(classes).Data("ui-card-body")[
            Context.Provide(scope with { Bare = true })[Children ?? []]
        ];
    }

    // The ring is a pseudo-element one pixel outside the panel, over its transparent border, so the panel's own
    // background stops short of it and a translucent fill never doubles up under the line.
    private const string Panel =
        "relative border border-transparent bg-clip-padding "
        + "before:pointer-events-none before:absolute before:-inset-px before:rounded-[inherit] before:inset-ring";

    private const string Well =
        "bg-zinc-50 before:inset-shadow-[0_1.5px_2.5px_rgb(0_0_0/0.05)] before:inset-ring-zinc-900/8 "
        + "dark:bg-black/15 dark:before:[--tw-inset-shadow:0_0_#0000] dark:before:inset-ring-white/8";

    // Next to a header the gap is the header's, so a bleed only closes what is left of it.
    private static string Seamless(UiCardScope scope) => scope.Roomy
        ? "not-first:[--ui-bleed-top:--spacing(2)] not-last:[--ui-bleed-bottom:--spacing(2)] "
            + "not-first:[--ui-bleed-top-radius:0px] not-last:[--ui-bleed-bottom-radius:0px]"
        : "not-first:[--ui-bleed-top:0px] not-last:[--ui-bleed-bottom:0px] "
            + "not-first:[--ui-bleed-top-radius:0px] not-last:[--ui-bleed-bottom-radius:0px]";

    private static string Inset(UiCardScope scope) => UiClass.Compose(
        Panel,
        "rounded-[calc(var(--ui-card-radius)-4px)]",
        "[--ui-bleed-top-radius:calc(var(--ui-card-radius)-5px)] [--ui-bleed-bottom-radius:calc(var(--ui-card-radius)-5px)]",
        // One pixel less than the header beside it: the panel's own border makes it up.
        scope.Roomy
            ? "px-[19px] py-6 [--ui-bleed-x:19px] [--ui-bleed:19px] [--ui-bleed-top:--spacing(6)] [--ui-bleed-bottom:--spacing(6)]"
            : "px-[11px] py-4 [--ui-bleed-x:11px] [--ui-bleed:11px] [--ui-bleed-top:--spacing(4)] [--ui-bleed-bottom:--spacing(4)]",
        scope.Tinted
            ? "bg-white shadow-xs before:inset-ring-zinc-900/8 dark:bg-white/4 dark:before:inset-ring-white/10"
            : Well);

    // Over the card's own border on every side it touches, which is what "flush" is.
    private static string Flush(UiCardScope scope) => UiClass.Compose(
        Panel,
        "z-1 -mx-px rounded-(--ui-card-radius) first:-mt-px last:-mb-px",
        scope.Roomy ? RoomyPadding : TightPadding,
        scope.Tinted
            ? "bg-white before:inset-ring-zinc-200 before:shadow-xs dark:bg-white/4 dark:before:inset-ring-white/10 dark:before:shadow-none"
            : Well);

    private static string Padded(UiCardScope scope) => UiClass.Compose(
        scope.Roomy ? RoomyPadding : TightPadding,
        "not-first:[--ui-bleed-top-radius:0px] not-last:[--ui-bleed-bottom-radius:0px]");

    private const string RoomyPadding =
        "p-6 [--ui-bleed-x:--spacing(6)] [--ui-bleed:--spacing(6)] [--ui-bleed-top:--spacing(6)] [--ui-bleed-bottom:--spacing(6)]";

    private const string TightPadding =
        "p-4 [--ui-bleed-x:--spacing(4)] [--ui-bleed:--spacing(4)] [--ui-bleed-top:--spacing(4)] [--ui-bleed-bottom:--spacing(4)]";
}
