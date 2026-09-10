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
                        UiButton.Label("Throw inside inner boundary").Icon(UiIconName.Warning).Tone(UiTone.Error)
                            .Id("boom-nested-throw")
                            .OnClick(ThrowFromInnerHandler)
                    ]
                ]
            ]
        ];

    private static Component InnerFallback(Exception ex, Action recover) =>
        UiAlert.Icon(UiIconName.ShieldWarning).Tone(UiTone.Warning).Variant(UiVariant.Soft).Class("flex items-start")
            .Id("boom-nested-inner-fallback")[Div[
                Strong["Inner boundary caught: "],
                Code.Class("ms-1")[ex.GetType().Name],
                P.Class("mb-2 mt-1 text-sm")[ex.Message],
                UiButton.Label("Recover inner").Icon(UiIconName.Undo).Variant(UiVariant.Outline)
                    .Id("boom-nested-inner-recover")
                    .OnClick(recover)
            ]];

    private static Component OuterFallback(Exception ex) =>
        UiAlert.Tone(UiTone.Error).Variant(UiVariant.Soft).Id("boom-nested-outer-fallback")[Strong["Outer boundary caught: "], ex.Message];

    private static void ThrowFromInnerHandler() =>
        throw new InvalidOperationException("kaboom — inner boundary demo");
}
