using Rask.Core.Components;

namespace Rask.Example.Shared.Features;

// Intermediate, theme-unaware component. Receives no theme prop and is render-cached after the
// first paint (no props, no state) — yet the badge it nests still updates on every toggle.
public sealed partial class ThemeCard : Component
{
    protected override Component? Render() =>
        BsStack(Gap: 2, Align: BsAlign.Center)[
            Span(Class: "small text-secondary")["Deeply nested, no theme prop passed in:"],
            ThemeBadge()
        ];
}
