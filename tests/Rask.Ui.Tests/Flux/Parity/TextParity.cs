using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary>fluxui.dev/components/text, example by example.</summary>
public sealed partial class TextParity : FluxParity
{
    private const string Column = "width:320px;display:flex;flex-direction:column;gap:24px";

    public override string Page => "text";

    public override IEnumerable<(string Section, Component Example)> Examples()
    {
        yield return ("", Div.Style("width:384px")[
            Ui.Heading["Text component"],
            Ui.Text.Style("margin-top:8px")[
                "This is the standard text component for body copy and general content throughout your application."]
        ]);

        // Flux sizes these with Tailwind's `text-base` and `text-xs`, which no kit sheet carries for a test; the
        // Size steps write the same two utilities.
        yield return ("size", Div.Style(Column)[
            Div[Ui.Text.Lg["Larger text size"]],
            Div[Ui.Text["Default text size"]],
            Div[Ui.Text.Sm["Smaller text"]]
        ]);

        yield return ("color", Div.Style(Column)[
            Div[Ui.Text.Variant(Ui.TextVariant.Strong)["Strong text color"]],
            Div[Ui.Text["Default text color"]],
            Div[Ui.Text.Subtle["Subtle text color"]],
            Div[Ui.Text.Color(Ui.Color.Blue)["Colored text"]]
        ]);

        yield return ("link", Div.Style("width:320px")[
            Ui.Text["Visit our ", Ui.Link.Href("#")["documentation"], " for more information."]
        ]);

        // The live page sets each link in a line of text of its own.
        yield return ("link-variants", Div.Style("width:320px")[
            Ui.Text.Style("margin-bottom:24px")[Ui.Link.Href("#")["Default link"]],
            Ui.Text.Style("margin-bottom:24px")[Ui.Link.Href("#").Ghost["Ghost link"]],
            Ui.Text[Ui.Link.Href("#").Subtle["Subtle link"]]
        ]);

        // Outside a text, so it takes the docs page's own prose size; `wire:click` is OnClick.
        yield return ("link-as-button", Div.Style("width:320px;font-size:16px;line-height:26px")[
            Ui.Link.As(Ui.LinkAs.Button)["Create new account →"]
        ]);
    }
}
