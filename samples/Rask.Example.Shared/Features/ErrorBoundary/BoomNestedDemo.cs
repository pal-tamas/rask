namespace Rask.Example.Shared.Features;

// Boundaries nest: the inner boundary catches first, so the outer healthy
// region (and its sibling paragraph) stays mounted. If the inner fallback
// itself throws, the outer boundary catches the escalation.
public sealed partial class BoomNestedDemo : Component
{
    protected override Component? Render() =>
        ErrorBoundary.Fallback((ex, _) => OuterFallback(ex))[
            Div.Class("p-3 border rounded bg-white").Id("boom-nested-host")[
                P
                    .Class("mb-2 text-sm text-slate-500 dark:text-slate-400")
                    .Id("boom-nested-outer-healthy")[
                    "Outer healthy region — stays mounted while the inner boundary trips."],
                ErrorBoundary.Fallback((ex, recover) => InnerFallback(ex, recover))[
                    Div.Class("p-3 border rounded bg-slate-100")[
                        P.Class("text-sm text-slate-500 dark:text-slate-400 mb-2")["Inner boundary subtree."],
                        Button.Type("button").Class(Tw.BtnDanger)
                            .Id("boom-nested-throw")
                            .OnClick(ThrowFromInnerHandler)[Icon.Name(IconName.ExclamationTriangle).Class("me-2"),
                            "Throw inside inner boundary"]
                    ]
                ]
            ]
        ];

    private static Component InnerFallback(Exception ex, Action recover) =>
        Div.Class($"{Tw.AlertWarning} flex items-start")
            .Id("boom-nested-inner-fallback")[
            Icon.Name(IconName.ShieldExclamation).Class("me-3 text-xl"),
            Div[
                Strong["Inner boundary caught: "],
                Code.Class("ms-1")[ex.GetType().Name],
                P.Class("mb-2 mt-1 text-sm")[ex.Message],
                Button.Type("button").Class(Tw.BtnOutlineSecondary)
                    .Id("boom-nested-inner-recover")
                    .OnClick(recover)[Icon.Name(IconName.ArrowCounterclockwise).Class("me-1"), "Recover inner"]
            ]
        ];

    private static Component OuterFallback(Exception ex) =>
        Div.Class(Tw.AlertDanger).Id("boom-nested-outer-fallback")[
            Strong["Outer boundary caught: "], ex.Message
        ];

    private static void ThrowFromInnerHandler() =>
        throw new InvalidOperationException("kaboom — inner boundary demo");
}
