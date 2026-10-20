namespace Rask.Site.Features.UiDocs;

/// <summary>
///     The same three components under four accents. Each box re-points the accent tokens for what is inside it, as an
///     app's own <c>@theme</c> does for the whole page.
/// </summary>
public sealed partial class UiThemingDemo : Component
{
    // Written out whole, because Tailwind finds a class by reading this file: one built from pieces is never compiled.
    private static readonly (string Name, string Tokens)[] Accents =
    [
        ("Default", ""),
        ("Blue", "[--color-fx-accent:var(--color-blue-500)] [--color-fx-accent-content:var(--color-blue-600)] "
                 + "[--color-fx-accent-foreground:var(--color-white)] dark:[--color-fx-accent-content:var(--color-blue-400)]"),
        ("Emerald", "[--color-fx-accent:var(--color-emerald-600)] [--color-fx-accent-content:var(--color-emerald-600)] "
                    + "[--color-fx-accent-foreground:var(--color-white)] dark:[--color-fx-accent-content:var(--color-emerald-400)]"),
        ("Rose", "[--color-fx-accent:var(--color-rose-500)] [--color-fx-accent-content:var(--color-rose-600)] "
                 + "[--color-fx-accent-foreground:var(--color-white)] dark:[--color-fx-accent-content:var(--color-rose-400)]"),
    ];

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Data("testid", "ui-theming").Class("grid gap-4 sm:grid-cols-2")[
            Accents.Select(accent => Ui.Card.Key(accent.Name).Class(accent.Tokens)[
                Ui.Heading.Accent()[accent.Name],
                Ui.Text.Class("mt-1 mb-4")["A ", Ui.Link.Href("#")["link"], " and a primary button."],
                Ui.Button.Primary.Sm["Save changes"]
            ])
        ];
}
