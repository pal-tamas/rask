namespace Rask;

/// <summary>
/// Buttons or other controls in a card's header or footer. Flux UI's <c>card.actions</c>.
/// </summary>
/// <remarks>
/// In a header they centre on the heading and tuck into the corner instead of making the header taller: the
/// negative margins are what let a 32px button sit on a 24px line. After the heading they sit at the end;
/// before it, at the start.
/// </remarks>
public sealed partial class UiCardActions : Component
{
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var scope = Context.Get<UiCardScope>() ?? UiCardScope.Standalone(null);
        var classes = UiClass.Compose(
            "col-start-3 row-start-1 [:where(&)]:ms-4 [:where(&)]:flex items-center [:where(&)]:gap-2",
            scope.Footer ? "self-center" : "row-span-2 self-start",
            scope.Roomy ? "[:where(&)]:-my-1" : "[:where(&)]:-my-1.5",
            End(scope),
            Start(scope),
            Class);

        return Div.Class(classes).Data("ui-card-actions")[Children ?? []];
    }

    // As far from the side as from the top: the edge's own padding, less what the button overhangs its line by.
    private static string End(UiCardScope scope) => scope.Edge switch
    {
        null => scope.Roomy ? "" : "[:where(&)]:-me-0.5",
        Ui.CardBodyVariant.Seamless => scope.Roomy ? "[:where(&)]:-me-1" : "[:where(&)]:-me-1.5",
        _ => scope.Size switch
        {
            Ui.CardSize.Xs => "[:where(&)]:-me-2.5",
            Ui.CardSize.Sm => "[:where(&)]:-me-1.5",
            _ => "[:where(&)]:-me-3",
        },
    };

    // The same, mirrored, for actions written before the heading. An only child is still at the end.
    private static string Start(UiCardScope scope) => UiClass.Compose(
        "[&:first-child:not(:only-child)]:col-start-1 [&:first-child:not(:only-child)]:[:where(&)]:me-4",
        scope.Edge switch
        {
            null => scope.Roomy ? "[&:first-child:not(:only-child)]:[:where(&)]:ms-0" : "[&:first-child:not(:only-child)]:[:where(&)]:-ms-0.5",
            Ui.CardBodyVariant.Seamless => scope.Roomy
                ? "[&:first-child:not(:only-child)]:[:where(&)]:-ms-1"
                : "[&:first-child:not(:only-child)]:[:where(&)]:-ms-1.5",
            _ => scope.Size switch
            {
                Ui.CardSize.Xs => "[&:first-child:not(:only-child)]:[:where(&)]:-ms-2.5",
                Ui.CardSize.Sm => "[&:first-child:not(:only-child)]:[:where(&)]:-ms-1.5",
                _ => "[&:first-child:not(:only-child)]:[:where(&)]:-ms-3",
            },
        });
}
