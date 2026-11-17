namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ModalDialogButtonsExample : Component
{
    private bool _booking;
    private string _last = "nothing booked yet";

    protected override Component? Render() =>
        Div.Class("flex flex-wrap items-center gap-4")[
            Ui.Button.OnClick(() => { _booking = true; })["Book stock"],
            Ui.Modal.Open(_booking).OnClose(() => { _booking = false; }).Class("md:w-96")[
                Div.Class("space-y-6")[
                    Div[
                        Ui.Heading.Level(2).Lg["Book 12 pallets?"],
                        Ui.Text.Class("mt-2")["They move into the warehouse today."]
                    ],
                    // Cancel closes the modal; Book runs the handler, busy while it waits.
                    Ui.DialogButtons.Save("Book").OnSave(async () =>
                    {
                        await Task.Delay(600);
                        _last = "booked 12 pallets";
                        _booking = false;
                    })
                ]
            ],
            Ui.Text[$"Last action: {_last}."]
        ];
}
