namespace Rask.Site.Features.UiDocs;

/// <summary>
///     Light, dark and system, and a toggle: each button sets Rask.appearance or Rask.dark in the browser, so not one of
///     them runs a C# handler, and they work before the app has booted.
/// </summary>
public sealed partial class UiDarkModeDemo : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Data("testid", "ui-dark-mode").Class("flex flex-wrap items-center gap-4")[
            Ui.ButtonGroup[
                Choice("light", Ui.IconName.Sun, "Light"),
                Choice("dark", Ui.IconName.Moon, "Dark"),
                Choice("system", Ui.IconName.ComputerDesktop, "System")
            ],
            Ui.Button.Subtle.Icon(Ui.IconName.Moon).Tooltip("Toggle dark mode")
                .Attributes(("onclick", "Rask.dark = !Rask.dark"))
        ];

    private static Component Choice(string appearance, Ui.IconName icon, string label) =>
        Ui.Button.Key(appearance).Icon(icon)
            .Attributes(("onclick", $"Rask.appearance = '{appearance}'"))[label];
}
