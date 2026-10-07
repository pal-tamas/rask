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

    // Flux's row is its theme button, the separator and a "Log in" button. The theme button is a subtle
    // square whose resting ink the docs page sets to zinc-300 with a class of its own — an APP's utility,
    // so it is stated here under this page's name. It carries Flux's `tooltip` prop, which wraps it in the
    // tooltip that names it: the one place a page measures a button's own tooltip.
    private static Component Bar(Component separator) =>
        Div.Style("display:flex;gap:24px;align-items:center;justify-content:center")[
            Raw.Value("<style>.parity-moon:not(:hover){color:var(--color-zinc-300)}</style>"),
            Ui.Button.Subtle.Icon(Ui.IconName.Moon).Class("parity-moon").Tooltip("Switch to dark theme"),
            separator,
            Ui.Button["Log in"]
        ];
}
