using Rask.Core.Components;

namespace Rask.Example.Shared.Features;

// Intermediate, theme-unaware component. Receives no theme prop and is render-cached after the
// first paint (no props, no state) — yet the badge it nests still updates on every toggle.
public sealed class ThemeCard : Component
{
    protected override Component? Render() =>
        Div(Class: "d-flex align-items-center gap-2")[
            Span(Class: "small text-secondary")["Deeply nested, no theme prop passed in:"],
            ThemeBadge()
        ];
}
