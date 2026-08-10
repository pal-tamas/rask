namespace Rask.Example.Shared.Features;

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
                P.Class("text-secondary small mb-2")["Healthy subtree — click to throw."],
                BsButton.Color(BsColor.Danger).Id("boom-throw").OnClick(ThrowFromHandler)[BsIcon.Name(BsIconName.ExclamationTriangle).Class("me-2"),
                    "Throw a handler exception"]
            ]
        ];

    private static Component BoundaryFallback(Exception ex, Callback recover) =>
        BsAlert.Color(BsColor.Danger).Class("d-flex align-items-start").Id("boom-fallback")[
            BsIcon.Name(BsIconName.ExclamationOctagonFill).Class("me-3 fs-4"),
            Div[
                Strong["Boundary caught: "],
                Code.Class("ms-1")[ex.GetType().Name],
                P.Class("mb-2 mt-1 small")[ex.Message],
                BsButton.Color(BsColor.Secondary).Outline(true).Size(BsSize.Sm).Id("boom-recover").OnClick(recover)[BsIcon.Name(BsIconName.ArrowCounterclockwise).Class("me-1"), "Recover"]
            ]
        ];

    private static void ThrowFromHandler() =>
        throw new InvalidOperationException("kaboom — handler boundary demo");
}
