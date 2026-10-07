namespace Rask.Site.Features;

// Boundaries nest: the inner boundary catches first, so the outer healthy
// region (and its sibling paragraph) stays mounted. If the inner fallback
// itself throws, the outer boundary catches the escalation.
public sealed partial class BoomNestedDemo : Component
{
    protected override Component? Render() =>
        ErrorBoundary.Fallback((ex, _) => OuterFallback(ex))[
            Div.Class("p-3 border rounded bg-white").Id("boom-nested-host")[
                P
                    .Class("mb-2 text-sm text-ui-muted")
                    .Id("boom-nested-outer-healthy")[
                    "Outer healthy region — stays mounted while the inner boundary trips."],
                ErrorBoundary.Fallback((ex, recover) => InnerFallback(ex, recover))[
                    Div.Class("p-3 border rounded bg-ui-well")[
                        P.Class("text-sm text-ui-muted mb-2")["Inner boundary subtree."],
                        Ui.Button.Error
                            .Id("boom-nested-throw")
                            .OnClick(ThrowFromInnerHandler)[Ui.Icon.Name(Ui.IconName.ExclamationTriangle), "Throw inside inner boundary"]
                    ]
                ]
            ]
        ];

    private static Component InnerFallback(Exception ex, Action recover) =>
        Ui.Callout.Warning.Icon(Ui.IconName.ShieldExclamation).Id("boom-nested-inner-fallback").Role("alert")
            .Actions(Ui.Button.Outline
                .Id("boom-nested-inner-recover")
                .OnClick(recover)[Ui.Icon.Name(Ui.IconName.ArrowUturnLeft), "Recover inner"])[
            Ui.CalloutHeading["Inner boundary caught: ", Code[ex.GetType().Name]],
            Ui.CalloutText[ex.Message]
        ];

    private static UiCallout OuterFallback(Exception ex) =>
        Ui.Callout.Danger.Id("boom-nested-outer-fallback").Role("alert").Heading("Outer boundary caught: ").Text(ex.Message);

    private static void ThrowFromInnerHandler() =>
        throw new InvalidOperationException("kaboom — inner boundary demo");
}
