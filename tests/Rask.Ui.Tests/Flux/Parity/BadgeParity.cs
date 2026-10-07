using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary>fluxui.dev/components/badge, example by example.</summary>
public sealed partial class BadgeParity : FluxParity
{
    private static readonly Ui.Color?[] Colors =
    [
        null, Ui.Color.Red, Ui.Color.Orange, Ui.Color.Amber, Ui.Color.Yellow, Ui.Color.Lime, Ui.Color.Green,
        Ui.Color.Emerald, Ui.Color.Teal, Ui.Color.Cyan, Ui.Color.Sky, Ui.Color.Blue, Ui.Color.Indigo,
        Ui.Color.Violet, Ui.Color.Purple, Ui.Color.Fuchsia, Ui.Color.Pink, Ui.Color.Rose,
    ];

    public override string Page => "badge";

    public override IEnumerable<(string Section, Component Example)> Examples()
    {
        yield return ("", Line(Ui.Badge.Color(Ui.Color.Lime)["New"]));

        yield return ("sizes", Row(
            Ui.Badge.Sm["Small"],
            Ui.Badge["Default"],
            Ui.Badge.Lg["Large"]));

        yield return ("icons", Line(
            Ui.Badge.Icon(Ui.IconName.UserCircle)["Users"],
            Ui.Badge.Icon(Ui.IconName.DocumentText)["Files"],
            Ui.Badge.IconTrailing(Ui.IconName.VideoCamera)["Videos"]));

        yield return ("rounded", Line(Ui.Badge.Rounded().Icon(Ui.IconName.User)["Users"]));

        yield return ("as-button", Line(Ui.Badge.As(Ui.BadgeAs.Button).Rounded().Icon(Ui.IconName.Plus).Lg["Amount"]));

        yield return ("with-close-button", Line(Ui.Badge["Admin", Ui.BadgeClose]));

        yield return ("colors", Wrapped([.. Colors.Select(color => Ui.Badge.Key(Name(color)).Color(color)[Name(color)])]));

        yield return ("solid-variant", Wrapped([.. Colors.Select(color => Ui.Badge.Key(Name(color)).Solid.Color(color)[Name(color)])]));

        yield return ("inset", Line(Div[
            Raw.Value("<style>.parity-mt-2{margin-top:8px}</style>"),
            Ui.Heading.Lg["Page builder ", Ui.Badge.Color(Ui.Color.Lime).Inset(Ui.Inset.Top | Ui.Inset.Bottom)["New"]],
            Ui.Text.Class("parity-mt-2")["Easily author new pages without leaving your browser."]
        ]));
    }

    // Flux's own row for these examples: 8px apart, each badge as tall as the line.
    private static Component Line(params Component[] items) =>
        Div.Style("display:flex;gap:8px;justify-content:center")[items];

    private static Component Wrapped(Component[] items) =>
        Div.Style("display:flex;gap:8px;flex-wrap:wrap;width:560px;margin:0 auto")[items];

    private static string Name(Ui.Color? color) => color?.ToString() ?? "Zinc";
}
