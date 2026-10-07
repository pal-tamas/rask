namespace Rask;

/// <summary>
/// The top of a card: a <see cref="UiCardHeading" />, an optional <see cref="UiCardSubheading" /> and optional
/// <see cref="UiCardActions" />. Flux UI's <c>card.header</c>.
/// </summary>
/// <remarks>
/// It works on its own too, as a section heading above a card, and inside a <see cref="UiCardBody" />, where it
/// titles a sub-section and takes none of the card's body treatment.
/// </remarks>
public sealed partial class UiCardHeader : Component
{
    /// <summary>Outside a card, the size of the card it sits beside. Inside one, the card's size applies.</summary>
    public Ui.CardSize? Size { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var scope = (Context.Get<UiCardScope>() ?? UiCardScope.Standalone(Size)) with { Footer = false };
        var classes = UiClass.Compose(Grid, Padding(scope), Treatment(scope), Class);

        return Div.Class(classes).Data(("ui-card-header", null), ("ui-card-standalone", null))[
            Context.Provide(scope)[Children ?? []]
        ];
    }

    // Start actions, the heading and what sits under it, end actions. Everything that is not an action is the
    // middle column, so a subheading lands under its heading without the caller wrapping the two.
    internal const string Grid =
        "grid grid-cols-[auto_minmax(0,1fr)_auto] items-center [&>:not([data-ui-card-actions])]:col-start-2";

    private static string Padding(UiCardScope scope) => scope.Edge switch
    {
        null => scope.Roomy ? "pt-1 pb-5" : "pt-1 pb-3",
        Ui.CardBodyVariant.Seamless => scope.Roomy ? "pb-6" : "pb-4",
        // The card's own hairline of padding is already around it.
        Ui.CardBodyVariant.Inset => scope.Size switch
        {
            Ui.CardSize.Xs => "rounded-t-[calc(var(--ui-card-radius)-5px)] px-3 pt-2 pb-3",
            Ui.CardSize.Sm => "rounded-t-[calc(var(--ui-card-radius)-5px)] px-3 pt-3 pb-4",
            _ => "rounded-t-[calc(var(--ui-card-radius)-5px)] px-5 pt-3 pb-4",
        },
        _ => scope.Size switch
        {
            Ui.CardSize.Xs => "rounded-t-[calc(var(--ui-card-radius)-1px)] px-4 py-3",
            Ui.CardSize.Sm => "rounded-t-[calc(var(--ui-card-radius)-1px)] p-4",
            _ => "rounded-t-[calc(var(--ui-card-radius)-1px)] px-6 py-4",
        },
    };

    private static string Treatment(UiCardScope scope) => scope.Edge switch
    {
        Ui.CardBodyVariant.Separated => "bg-zinc-900/3 dark:bg-black/15",
        Ui.CardBodyVariant.Divided when scope.Divider == Ui.CardDivider.Full => "border-b border-zinc-900/5 dark:border-white/10",
        // The border still takes its pixel, so the two dividers leave the body the same height.
        Ui.CardBodyVariant.Divided => UiClass.Compose(
            "relative border-b border-transparent after:absolute after:top-full after:-bottom-px after:border-b",
            "after:border-zinc-900/5 dark:after:border-white/10",
            scope.Roomy ? "after:inset-x-6" : "after:inset-x-4"),
        _ => "",
    };
}
