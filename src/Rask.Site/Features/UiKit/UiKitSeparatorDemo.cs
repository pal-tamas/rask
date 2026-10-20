namespace Rask.Site.Features.UiKit;

/// <summary>Flux UI's separator page, example for example: plain, with text, vertical, limited height, subtle.</summary>
public sealed partial class UiKitSeparatorDemo : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Data("testid", "ui-separator").Class("mx-auto max-w-sm")[
            UiKitExample.Key("plain").Heading("Separator")[Ui.Separator],
            UiKitExample.Key("text").Heading("With text")[Ui.Separator.Text("or")],
            UiKitExample.Key("vertical").Heading("Vertical")
                .Blurb("As tall as its row.")[Row("vertical", Ui.Separator.Vertical())],
            UiKitExample.Key("limited").Heading("Limited height")
                .Blurb("Vertical margin shortens it.")[Row("limited", Ui.Separator.Vertical().Class("my-2"))],
            UiKitExample.Key("subtle").Heading("Subtle")
                .Blurb("A fainter line, for a toolbar's own groups.")[Row("subtle", Ui.Separator.Vertical().Subtle)]
        ];

    private static Component Row(string key, UiSeparator separator) =>
        Div.Class("flex items-center justify-center gap-6")[
            Ui.Button.Ghost.Key("theme").Icon(Ui.IconName.Moon).AriaLabel("Switch to dark theme"),
            separator.Key(key + "-separator"),
            Ui.Button.Key("login")["Log in"]
        ];
}
