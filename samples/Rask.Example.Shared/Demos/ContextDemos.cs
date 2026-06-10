using Rask.Core.Components;

namespace Rask.Example.Shared.Demos;

// A value provided high in the tree and read deep in the tree, with an intermediate component
// (ThemeCard) that knows nothing about the theme — no prop drilling. Toggling re-renders the
// provider's owner; the deep consumer (ThemeBadge) bypasses the render cache and picks up the
// new value even though the intermediate ThemeCard stays cached between the two.

public sealed record Theme(string Name, bool IsDark)
{
    public static readonly Theme Light = new("Light", false);
    public static readonly Theme Dark = new("Dark", true);
}

public sealed class ContextThemeDemo : Component
{
    private Theme _theme = Theme.Light;

    protected override RenderResult Render() =>
        // Provide the current theme to the whole subtree below.
        Context.Provide<Theme>(_theme)[
            Div(
                Class: "border rounded p-3",
                Style: _theme.IsDark ? "background:#212529;color:#e9ecef" : "background:#f8f9fa")[
                Button(
                    Class: "btn btn-sm btn-outline-secondary mb-3",
                    OnClick: () => _theme = _theme.IsDark ? Theme.Light : Theme.Dark)[
                    $"Toggle theme — currently {_theme.Name}"
                ],
                // ThemeCard has no idea a theme exists; it just renders structure + a badge.
                ThemeCard()
            ]
        ];
}

// Intermediate, theme-unaware component. Receives no theme prop and is render-cached after the
// first paint (no props, no state) — yet the badge it nests still updates on every toggle.
public sealed class ThemeCard : Component
{
    protected override RenderResult Render() =>
        Div(Class: "d-flex align-items-center gap-2")[
            Span(Class: "small text-secondary")["Deeply nested, no theme prop passed in:"],
            ThemeBadge()
        ];
}

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
