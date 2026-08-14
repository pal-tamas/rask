using Rask.Core.Components;

namespace Rask.Example.Shared.Features;

// A value provided high in the tree and read deep in the tree, with an intermediate component
// (ThemeCard) that knows nothing about the theme — no prop drilling. Toggling re-renders the
// provider's owner; the deep consumer (ThemeBadge) bypasses the render cache and picks up the
// new value even though the intermediate ThemeCard stays cached between the two.

public sealed record Theme(string Name, bool IsDark)
{
    public static readonly Theme Light = new("Light", false);
    public static readonly Theme Dark = new("Dark", true);
}

public sealed partial class ContextThemeDemo : Component
{
    private Theme _theme = Theme.Light;

    protected override Component? Render() =>
        // Provide the current theme to the whole subtree below.
        Context.Provide<Theme>(_theme)[
            Div
                .Class("border rounded p-3")
                .Style(_theme.IsDark ? "background:#212529;color:#e9ecef" : "background:#f8f9fa")[
                BsButton
                    .Color(BsColor.Secondary)
                    .Outline(true)
                    .Size(BsSize.Sm)
                    .Class("mb-3")
                    .OnClick(() => _theme = _theme.IsDark ? Theme.Light : Theme.Dark)[
                    $"Toggle theme — currently {_theme.Name}"
                ],
                // ThemeCard has no idea a theme exists; it just renders structure + a badge.
                ThemeCard
            ]
        ];
}
