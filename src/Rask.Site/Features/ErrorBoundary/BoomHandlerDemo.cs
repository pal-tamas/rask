namespace Rask.Site.Features;

// ErrorBoundary catches exceptions thrown by a descendant's event handler and
// renders the Fallback in place of the subtree. The fallback receives a
// recover() callback that clears the boundary's error and re-renders the
// healthy subtree. The fallback is also handed the exception and prints none of
// it: a message can carry a path, a query or a user's data, and it is already in
// the log. It says what happened in the visitor's words and what they can do.
public sealed partial class BoomHandlerDemo : Component
{
    protected override Component? Render() =>
        ErrorBoundary
            .Fallback((_, recover) => BoundaryFallback(recover))[
            Div.Class("p-3 border rounded bg-white").Id("boom-handler-host")[
                P.Class("text-ui-muted text-sm mb-2")["Healthy subtree — click to throw."],
                Ui.Button.Danger.Icon(Ui.IconName.ExclamationTriangle).Id("boom-throw").OnClick(ThrowFromHandler)["Throw a handler exception"]
            ]
        ];

    private static Component BoundaryFallback(Action recover) =>
        Ui.Callout.Danger.Icon(Ui.IconName.ExclamationTriangle).Id("boom-fallback").Role("alert")
            .Actions(Ui.Button.Icon(Ui.IconName.ArrowUturnLeft).Id("boom-recover").OnClick(recover)["Recover"])[
            Ui.CalloutHeading["That did not work."],
            Ui.CalloutText["The rest of the page is still running. Recover brings this part back."]
        ];

    private static void ThrowFromHandler() =>
        throw new InvalidOperationException("kaboom — handler boundary demo");
}
