namespace Rask.Site.Features;

// ErrorBoundary catches exceptions thrown by a descendant's event handler and
// renders the Fallback in place of the subtree. The fallback receives a
// recover() callback that clears the boundary's error and re-renders the
// healthy subtree.
public sealed partial class BoomHandlerDemo : Component
{
    protected override Component? Render() =>
        ErrorBoundary
            .Fallback(BoundaryFallback)[
            Div.Class("p-3 border rounded bg-white").Id("boom-handler-host")[
                P.Class("text-ui-muted text-sm mb-2")["Healthy subtree — click to throw."],
                UiButton.Label("Throw a handler exception").Icon(UiIconName.Warning).Tone(UiTone.Error).Id("boom-throw").OnClick(ThrowFromHandler)
            ]
        ];

    private static Component BoundaryFallback(Exception ex, Action recover) =>
        UiAlert.Icon(UiIconName.Warning).Tone(UiTone.Error).Variant(UiVariant.Soft).Class("flex items-start").Id("boom-fallback")[Div[
                Strong["Boundary caught: "],
                Code.Class("ms-1")[ex.GetType().Name],
                P.Class("mb-2 mt-1 text-sm")[ex.Message],
                UiButton.Label("Recover").Icon(UiIconName.Undo).Variant(UiVariant.Outline).Id("boom-recover").OnClick(recover)
            ]];

    private static void ThrowFromHandler() =>
        throw new InvalidOperationException("kaboom — handler boundary demo");
}
