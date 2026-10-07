using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary>
///     <c>fluxui.dev/components/context</c>: the one example on that page.
/// </summary>
/// <remarks>
///     The area is Flux's dashed card around a line of text. <c>scripts/flux/parity-menu.mjs</c> right-clicks
///     it on both pages and compares the menu that opens at the pointer.
/// </remarks>
public sealed partial class ContextParity : FluxParity
{
    private sealed class Model
    {
        public string Sort { get; set; } = "name";
    }

    private readonly Model _model = new();

    public override string Page => "context";

    public override IEnumerable<(string Section, Component Example)> Examples()
    {
        // `border-dashed border-2 px-16` is the example's own, an app's classes: stated here under another name.
        yield return ("", Row(Div[
            Raw.Value("<style>.parity-area{border-style:dashed;border-width:2px;padding-inline:4rem}</style>"),
            Ui.Context[
                Ui.Card.Class("parity-area")[Ui.Text["Right click"]],
                Ui.Menu[
                    Ui.MenuItem.Icon(Ui.IconName.Plus)["New post"],
                    Ui.MenuSeparator,
                    Ui.MenuSubmenu.Heading("Sort by")[
                        Ui.MenuRadioGroup.Bind(() => _model.Sort)[
                            Ui.MenuRadio.Value("name")["Name"],
                            Ui.MenuRadio.Value("date")["Date"],
                            Ui.MenuRadio.Value("popularity")["Popularity"]
                        ]
                    ],
                    Ui.MenuSubmenu.Heading("Filter")[
                        Ui.MenuCheckbox.Value(true)["Draft"],
                        Ui.MenuCheckbox.Value(true)["Published"],
                        Ui.MenuCheckbox.Value(false)["Archived"]
                    ],
                    Ui.MenuSeparator,
                    Ui.MenuItem.Danger.Icon(Ui.IconName.Trash)["Delete"]
                ]
            ]
        ]));
    }
}
