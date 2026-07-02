namespace Rask.Example.Shared.Features;

// Boundaries nest: the inner boundary catches first, so the outer healthy
// region (and its sibling paragraph) stays mounted. If the inner fallback
// itself throws, the outer boundary catches the escalation.
public sealed class BoomNestedDemo : Component
{
    protected override Component? Render() =>
        ErrorBoundary((ex, _) => OuterFallback(ex))[
            Div(Class: "p-3 border rounded bg-white", Id: "boom-nested-host")[
                P(Class: "mb-2 small text-secondary",
                    Id: "boom-nested-outer-healthy")[
                    "Outer healthy region — stays mounted while the inner boundary trips."],
                ErrorBoundary((ex, recover) => InnerFallback(ex, recover))[
                    Div(Class: "p-3 border rounded bg-light")[
                        P(Class: "small text-secondary mb-2")["Inner boundary subtree."],
                        BsButton(Color: BsColor.Danger, Size: BsSize.Sm, Id: "boom-nested-throw", OnClick: ThrowFromInnerHandler)[BsIcon(Name: BsIconName.ExclamationTriangle, Class: "me-2"),
                            "Throw inside inner boundary"]
                    ]
                ]
            ]
        ];

    private static Component InnerFallback(Exception ex, Callback recover) =>
        BsAlert(Color: BsColor.Warning, Class: "d-flex align-items-start",
            Id: "boom-nested-inner-fallback")[
            BsIcon(Name: BsIconName.ShieldExclamation, Class: "me-3 fs-4"),
            Div()[
                Strong()["Inner boundary caught: "],
                Code(Class: "ms-1")[ex.GetType().Name],
                P(Class: "mb-2 mt-1 small")[ex.Message],
                BsButton(Color: BsColor.Secondary, Outline: true, Size: BsSize.Sm, Id: "boom-nested-inner-recover", OnClick: recover)[BsIcon(Name: BsIconName.ArrowCounterclockwise, Class: "me-1"), "Recover inner"]
            ]
        ];

    private static Component OuterFallback(Exception ex) =>
        BsAlert(Color: BsColor.Danger, Id: "boom-nested-outer-fallback")[
            Strong()["Outer boundary caught: "], ex.Message
        ];

    private static void ThrowFromInnerHandler() =>
        throw new InvalidOperationException("kaboom — inner boundary demo");
}
