namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ModalExample : Component
{
    private int _closed;

    protected override Component? Render() =>
        Div.Class("flex flex-wrap items-center gap-4")[
            Ui.ModalTrigger.Name("edit-profile")[Ui.Button["Edit profile"]],
            Ui.Modal.Name("edit-profile").Class("md:w-96").OnClose(() => { _closed++; })[
                Div.Class("space-y-6")[
                    Div[
                        Ui.Heading.Level(2).Lg["Update profile"],
                        Ui.Text.Class("mt-2")["Make changes to your personal details."]
                    ],
                    Ui.Input.Of<string>().Label("Name").Placeholder("Your name"),
                    Ui.Input.Of<string>().Label("Date of birth").Type(InputType.Date),
                    // Opening this inside the dialog is not the dialog closing.
                    Details.Data("testid", "ui-modal-more")[
                        Summary["More"],
                        P["Opening this does not close the dialog."]
                    ],
                    Div.Class("flex")[Ui.Spacer, Ui.Button.Primary["Save changes"]]
                ]
            ],
            Ui.Text[$"Closed {_closed} time{(_closed == 1 ? "" : "s")}."]
        ];
}
