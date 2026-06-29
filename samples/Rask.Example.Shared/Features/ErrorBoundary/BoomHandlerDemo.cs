namespace Rask.Example.Shared.Features;

// ErrorBoundary catches exceptions thrown by a descendant's event handler and
// renders the Fallback in place of the subtree. The fallback receives a
// recover() callback that clears the boundary's error and re-renders the
// healthy subtree.
public sealed class BoomHandlerDemo : Component
{
    protected override RenderResult Render() =>
        ErrorBoundary(
            BoundaryFallback)[
            Div(Class: "p-3 border rounded bg-white", Id: "boom-handler-host")[
                P(Class: "text-secondary small mb-2")["Healthy subtree — click to throw."],
                Button(
                    Class: "btn btn-danger",
                    Id: "boom-throw",
                    OnClick: ThrowFromHandler)[I(Class: "bi bi-exclamation-triangle me-2"),
                    "Throw a handler exception"]
            ]
        ];

    private static Child BoundaryFallback(Exception ex, Callback recover) =>
        BsAlert(Color: BsColor.Danger, Class: "d-flex align-items-start", Id: "boom-fallback")[
            I(Class: "bi bi-exclamation-octagon-fill me-3 fs-4"),
            Div()[
                Strong()["Boundary caught: "],
                Code(Class: "ms-1")[ex.GetType().Name],
                P(Class: "mb-2 mt-1 small")[ex.Message],
                Button(
                    Class: "btn btn-sm btn-outline-secondary",
                    Id: "boom-recover",
                    OnClick: recover)[I(Class: "bi bi-arrow-counterclockwise me-1"), "Recover"]
            ]
        ];

    private static void ThrowFromHandler() =>
        throw new InvalidOperationException("kaboom — handler boundary demo");
}
