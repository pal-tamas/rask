using Rask.Core.Components;
using Rask.Html.Components;

namespace Rask.Example.Shared.Features;

// Intermediate, theme-unaware component. Receives no theme prop and is render-cached after the
// first paint (no props, no state) — yet the badge it nests still updates on every toggle.
public sealed partial class ThemeCard : Component
{
    protected override Component? Render() =>
        Div.Class("flex gap-2 items-center flex-wrap items-center")[
            Span.Class("text-sm text-slate-500 dark:text-slate-400")["Deeply nested, no theme prop passed in:"],
            ThemeBadge
        ];
}
