namespace Rask.Site.Features;

// Call site: invoke it like any method — no generated factory, no reconciliation identity.
public sealed partial class TierStaticHelperDemo : Component
{
    protected override Component? Render() =>
        Div.Class("flex gap-2 flex-wrap items-center")[
            TierStaticHelper.Badge("inlined"),
            TierStaticHelper.Badge("no state"),
            TierStaticHelper.Badge("no lifecycle")
        ];
}
