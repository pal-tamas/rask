namespace Rask.Site.Features;

// Boundaries nest: the inner boundary catches first, so the outer healthy
// region (and its sibling paragraph) stays mounted. If the inner fallback
// itself throws, the outer boundary catches the escalation. Neither fallback
// prints the exception it is handed.
public sealed partial class BoomNestedDemo : Component
{
    protected override Component? Render() =>
        ErrorBoundary.Fallback((_, _) => OuterFallback())[
            Div.Class("p-3 border rounded bg-white").Id("boom-nested-host")[
                P
                    .Class("mb-2 text-sm text-ui-muted")
                    .Id("boom-nested-outer-healthy")[
                    "Outer healthy region — stays mounted while the inner boundary trips."],
                ErrorBoundary.Fallback((_, recover) => InnerFallback(recover))[
                    Div.Class("p-3 border rounded bg-ui-well")[
                        P.Class("text-sm text-ui-muted mb-2")["Inner boundary subtree."],
                        Ui.Button.Danger.Icon(Ui.IconName.ExclamationTriangle)
                            .Id("boom-nested-throw")
                            .OnClick(ThrowFromInnerHandler)["Throw inside inner boundary"]
                    ]
                ]
            ]
        ];

    private static Component InnerFallback(Action recover) =>
        Ui.Callout.Warning.Icon(Ui.IconName.ShieldExclamation).Id("boom-nested-inner-fallback").Role("alert")
            .Actions(Ui.Button.Icon(Ui.IconName.ArrowUturnLeft)
                .Id("boom-nested-inner-recover")
                .OnClick(recover)["Recover inner"])[
            Ui.CalloutHeading["The inner part stopped."],
            Ui.CalloutText["Everything around it is still running. Recover inner brings it back."]
        ];

    private static UiCallout OuterFallback() =>
        Ui.Callout.Danger.Id("boom-nested-outer-fallback").Role("alert")
            .Heading("This example stopped.").Text("Reload the page to bring it back.");

    private static void ThrowFromInnerHandler() =>
        throw new InvalidOperationException("kaboom — inner boundary demo");
}
