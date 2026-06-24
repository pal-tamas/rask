namespace Rask.Example.Shared.Features;

// Boundaries nest: the inner boundary catches first, so the outer healthy
// region (and its sibling paragraph) stays mounted. If the inner fallback
// itself throws, the outer boundary catches the escalation.
public sealed class BoomNestedDemo : Component
{
    protected override RenderResult Render() =>
        ErrorBoundary((ex, _) => OuterFallback(ex))[
            Div(Class: "p-3 border rounded bg-white", Id: "boom-nested-host")[
                P(Class: "mb-2 small text-secondary",
                    Id: "boom-nested-outer-healthy")[
                    "Outer healthy region — stays mounted while the inner boundary trips."],
                ErrorBoundary((ex, recover) => InnerFallback(ex, recover))[
                    Div(Class: "p-3 border rounded bg-light")[
                        P(Class: "small text-secondary mb-2")["Inner boundary subtree."],
                        Button(
                            Class: "btn btn-danger btn-sm",
                            Id: "boom-nested-throw",
                            OnClick: ThrowFromInnerHandler)[I(Class: "bi bi-exclamation-triangle me-2"),
                            "Throw inside inner boundary"]
                    ]
                ]
            ]
        ];

    private static Child InnerFallback(Exception ex, Callback recover) =>
        Div(Class: "alert alert-warning d-flex align-items-start",
            Id: "boom-nested-inner-fallback")[
            I(Class: "bi bi-shield-exclamation me-3 fs-4"),
            Div()[
                Strong()["Inner boundary caught: "],
                Code(Class: "ms-1")[ex.GetType().Name],
                P(Class: "mb-2 mt-1 small")[ex.Message],
                Button(
                    Class: "btn btn-sm btn-outline-secondary",
                    Id: "boom-nested-inner-recover",
                    OnClick: recover)[I(Class: "bi bi-arrow-counterclockwise me-1"), "Recover inner"]
            ]
        ];

    private static Child OuterFallback(Exception ex) =>
        Div(Class: "alert alert-danger", Id: "boom-nested-outer-fallback")[
            Strong()["Outer boundary caught: "], ex.Message
        ];

    private static void ThrowFromInnerHandler() =>
        throw new InvalidOperationException("kaboom — inner boundary demo");
}
