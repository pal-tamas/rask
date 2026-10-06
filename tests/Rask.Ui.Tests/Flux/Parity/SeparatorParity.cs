using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary>fluxui.dev/components/separator, example for example.</summary>
public sealed partial class SeparatorParity : FluxParity
{
    public override string Page => "separator";

    public override IEnumerable<(string Section, Component Example)> Examples()
    {
        yield return ("", Column(Ui.Separator));
        yield return ("with-text", Column(Ui.Separator.Text("or")));
        yield return ("vertical", Bar(Ui.Separator.Vertical()));
        yield return ("limited-height", Bar(Ui.Separator.Vertical().Class("my-2")));
        yield return ("subtle", Bar(Ui.Separator.Vertical().Subtle));
    }

    // Flux's docs page gives an example a column this wide, and a horizontal separator fills it.
    private static Component Column(Component separator) =>
        Div.Style("width:384px;margin:0 auto")[separator];

    // Flux's row is its theme button, the separator and a "Log in" button. Both buttons are other
    // components, so they are plain stand-ins of the same height here — unmarked, and parity.mjs says they
    // were not compared. They become Ui.Button when the button is rebuilt.
    private static Component Bar(Component separator) =>
        Div.Style("display:flex;gap:24px;align-items:center;justify-content:center")[
            Button.Type(ButtonType.Button).Style("width:40px;height:40px"),
            separator,
            Button.Type(ButtonType.Button).Style("height:40px;padding:0 16px")["Log in"]
        ];
}
