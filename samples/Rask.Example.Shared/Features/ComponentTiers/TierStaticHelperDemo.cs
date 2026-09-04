namespace Rask.Example.Shared.Features;

// Tier 0 — a plain static method. There is no Component subclass here, so there is no
// instance: no persistent state, no lifecycle hooks, no independent render cache. The markup
// it returns is inlined into whatever calls it, on every render of that caller. It is the
// cheapest way to factor out repeated markup — but because it has no instance it can neither
// hold state nor safely latch onto ambient context (Context.Get). Promote it to a Component
// (Tier 1) the moment you need any of those.
[global::Rask.Core.RaskMarkup]
internal static partial class TierStaticHelper
{
    public static Component Badge(string label) =>
        Span.Class(Tw.BadgeSecondary)[label];
}

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
