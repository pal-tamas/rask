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
                Ui.Button.Danger.Icon(Ui.IconName.ExclamationTriangle).Id("boom-throw").OnClick(ThrowFromHandler)["Throw a handler exception"]
            ]
        ];

    private static Component BoundaryFallback(Exception ex, Action recover) =>
        Ui.Callout.Danger.Icon(Ui.IconName.ExclamationTriangle).Id("boom-fallback").Role("alert")
            .Actions(Ui.Button.Icon(Ui.IconName.ArrowUturnLeft).Id("boom-recover").OnClick(recover)["Recover"])[
            Ui.CalloutHeading["Boundary caught: ", Code[ex.GetType().Name]],
            Ui.CalloutText[ex.Message]
        ];

    private static void ThrowFromHandler() =>
        throw new InvalidOperationException("kaboom — handler boundary demo");
}
