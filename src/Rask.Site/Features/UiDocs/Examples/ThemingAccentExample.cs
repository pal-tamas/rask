namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ThemingAccentExample : Component
{
    protected override Component? Render() =>
        Div.Class("grid gap-4 sm:grid-cols-2")[
            Swatch("Default", ""),
            Swatch("Blue", "[--color-fx-accent:var(--color-blue-500)] [--color-fx-accent-content:var(--color-blue-600)] "
                           + "dark:[--color-fx-accent-content:var(--color-blue-400)]"),
            Swatch("Emerald", "[--color-fx-accent:var(--color-emerald-600)] [--color-fx-accent-content:var(--color-emerald-600)] "
                              + "dark:[--color-fx-accent-content:var(--color-emerald-400)]"),
            Swatch("Rose", "[--color-fx-accent:var(--color-rose-500)] [--color-fx-accent-content:var(--color-rose-600)] "
                           + "dark:[--color-fx-accent-content:var(--color-rose-400)]")
        ];

    private static Component Swatch(string name, string accent) =>
        Ui.Card.Key(name).Class(accent + " [--color-fx-accent-foreground:var(--color-white)]")[
            Ui.Heading.Accent()[name],
            Ui.Text.Class("mt-1 mb-4")["A ", Ui.Link.Href("#")["link"], " and a primary button."],
            Ui.Button.Primary.Sm["Save changes"]
        ];
}
