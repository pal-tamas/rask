using Rask.Core.Components;
using Rask.Html.Components;

namespace Rask.Example.Shared.Features;

// The consumer. Reads the nearest provided Theme; calling Context.Required marks it as a context
// consumer, so it re-renders whenever the provided value changes.
public sealed partial class ThemeBadge : Component
{
    protected override Component? Render()
    {
        var theme = Context.Required<Theme>();
        var css = theme.IsDark
            ? "bg-slate-900 text-slate-100 ring-1 ring-slate-600"
            : "bg-amber-100 text-amber-900";
        return Span.Class($"{Ui.BadgeSecondary} {css}")[theme.IsDark ? "🌙 Dark" : "☀️ Light"];
    }
}
