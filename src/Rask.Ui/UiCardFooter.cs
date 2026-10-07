namespace Rask;

/// <summary>
/// The bottom of a card: supporting text and optional <see cref="UiCardActions" />. Flux UI's <c>card.footer</c>.
/// </summary>
/// <remarks>
/// The header's mirror image — the same columns, the same treatment, on the card's other edge. Like a header it
/// works outside a card, and inside a <see cref="UiCardBody" /> it closes a sub-section.
/// </remarks>
public sealed partial class UiCardFooter : Component
{
    /// <summary>Outside a card, the size of the card it sits beside. Inside one, the card's size applies.</summary>
    public Ui.CardSize? Size { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var scope = (Context.Get<UiCardScope>() ?? UiCardScope.Standalone(Size)) with { Footer = true };
        var classes = UiClass.Compose(UiCardHeader.Grid, Padding(scope), Treatment(scope), Class);

        return Div.Class(classes).Data(("ui-card-footer", null), ("ui-card-standalone", null))[
            Context.Provide(scope)[Children ?? []]
        ];
    }

    private static string Padding(UiCardScope scope) => scope.Edge switch
    {
        null => scope.Roomy ? "pt-5 pb-1" : "pt-3 pb-1",
        Ui.CardBodyVariant.Seamless => scope.Roomy ? "pt-6" : "pt-4",
        Ui.CardBodyVariant.Inset => scope.Size switch
        {
            Ui.CardSize.Xs => "rounded-b-[calc(var(--ui-card-radius)-5px)] px-3 pt-3 pb-2",
            Ui.CardSize.Sm => "rounded-b-[calc(var(--ui-card-radius)-5px)] px-3 pt-4 pb-3",
            _ => "rounded-b-[calc(var(--ui-card-radius)-5px)] px-5 pt-4 pb-3",
        },
        _ => scope.Size switch
        {
            Ui.CardSize.Xs => "rounded-b-[calc(var(--ui-card-radius)-1px)] px-4 py-3",
            Ui.CardSize.Sm => "rounded-b-[calc(var(--ui-card-radius)-1px)] p-4",
            _ => "rounded-b-[calc(var(--ui-card-radius)-1px)] px-6 py-4",
        },
    };

    private static string Treatment(UiCardScope scope) => scope.Edge switch
    {
        Ui.CardBodyVariant.Separated => "bg-zinc-900/3 dark:bg-black/15",
        Ui.CardBodyVariant.Divided when scope.Divider == Ui.CardDivider.Full => "border-t border-zinc-900/5 dark:border-white/10",
        Ui.CardBodyVariant.Divided => UiClass.Compose(
            "relative border-t border-transparent after:absolute after:bottom-full after:-top-px after:border-t",
            "after:border-zinc-900/5 dark:after:border-white/10",
            scope.Roomy ? "after:inset-x-6" : "after:inset-x-4"),
        _ => "",
    };
}
