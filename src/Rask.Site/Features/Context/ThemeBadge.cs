using Rask.Core.Components;

namespace Rask.Site.Features;

// The consumer. Reads the nearest provided Theme; calling Context.Required marks it as a context
// consumer, so it re-renders whenever the provided value changes.
public sealed partial class ThemeBadge : Component
{
    protected override Component? Render()
    {
        var theme = Context.Required<Theme>();
        return Ui.Badge
            .Solid
            .Color(theme.IsDark ? Ui.Color.Slate : Ui.Color.Amber)
            .Class("theme-badge")[theme.IsDark ? "🌙 Dark" : "☀️ Light"];
    }
}
