using Rask.Core.Components;

namespace Rask.Example.Shared.Features;

// The consumer. Reads the nearest provided Theme; calling Context.Required marks it as a context
// consumer, so it re-renders whenever the provided value changes.
public sealed class ThemeBadge : Component
{
    protected override RenderResult Render()
    {
        var theme = Context.Required<Theme>();
        var css = theme.IsDark ? "bg-dark text-light border border-secondary" : "bg-warning-subtle text-dark";
        return Span(Class: $"badge {css}")[theme.IsDark ? "🌙 Dark" : "☀️ Light"];
    }
}
