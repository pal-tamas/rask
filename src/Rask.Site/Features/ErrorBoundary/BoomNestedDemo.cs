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
                        Ui.Button.Tone(Ui.Tone.Error)
                            .Id("boom-nested-throw")
                            .OnClick(ThrowFromInnerHandler)[Ui.Icon.Name(Ui.IconName.Warning), "Throw inside inner boundary"]
                    ]
                ]
            ]
        ];

    private static Component InnerFallback(Exception ex, Action recover) =>
        Ui.Alert.Tone(Ui.Tone.Warning).Variant(Ui.Variant.Soft).Class("flex items-start")
            .Id("boom-nested-inner-fallback")[Ui.Icon.Name(Ui.IconName.ShieldWarning), Div[
                Strong["Inner boundary caught: "],
                Code.Class("ms-1")[ex.GetType().Name],
                P.Class("mb-2 mt-1 text-sm")[ex.Message],
                Ui.Button.Variant(Ui.Variant.Outline)
                    .Id("boom-nested-inner-recover")
                    .OnClick(recover)[Ui.Icon.Name(Ui.IconName.Undo), "Recover inner"]
            ]];

    private static Component OuterFallback(Exception ex) =>
        Ui.Alert.Tone(Ui.Tone.Error).Variant(Ui.Variant.Soft).Id("boom-nested-outer-fallback")[Strong["Outer boundary caught: "], ex.Message];

    private static void ThrowFromInnerHandler() =>
        throw new InvalidOperationException("kaboom — inner boundary demo");
}
