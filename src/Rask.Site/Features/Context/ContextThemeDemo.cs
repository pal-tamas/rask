using Rask.Core.Components;

namespace Rask.Site.Features;

public sealed partial class ContextThemeDemo : Component
{
    private Theme _theme = Theme.Light;

    protected override Component? Render() =>
        // Provide the current theme to the whole subtree below.
        Context.Provide<Theme>(_theme)[
            Div
                .Class("border rounded p-3")
                .Style(_theme.IsDark ? "background:#212529;color:#e9ecef" : "background:#f8f9fa")[
                Ui.Button.Variant(Ui.Variant.Outline).Class("mb-3")
                    .OnClick(() => _theme = _theme.IsDark ? Theme.Light : Theme.Dark)[$"Toggle theme — currently {_theme.Name}"],
                // ThemeCard has no idea a theme exists; it just renders structure + a badge.
                ThemeCard
            ]
        ];
}
