using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary>
///     <c>fluxui.dev/components/context</c>: the one example on that page.
/// </summary>
/// <remarks>
///     The area is a stand-in (<c>data-parity-skip</c>) for Flux's dashed card, which is the card page's to
///     match: a context menu needs only the room it takes. <c>scripts/flux/parity-menu.mjs</c> right-clicks it
///     on both pages and compares the menu that opens at the pointer.
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
        yield return ("", Row(Div[
            Ui.Context[
                Div.Style("width:200.25px;height:72px;border:2px dashed;border-radius:12px").Data("parity-skip", ""),
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
