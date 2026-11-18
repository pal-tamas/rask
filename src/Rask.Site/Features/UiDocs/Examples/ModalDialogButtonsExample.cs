using System.ComponentModel.DataAnnotations;

namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ModalDialogButtonsExample : Component
{
    private readonly Merger _merger = new();
    private bool _booking;
    private bool _merging;
    private string _last = "nothing booked yet";

    protected override Component? Render() =>
        Div.Class("flex flex-wrap items-center gap-4")[
            Ui.Button.OnClick(() => { _booking = true; })["Book stock"],
            // Heading and Text open it as Flux's examples do, the heading clear of the close button.
            Ui.Modal.Open(_booking).OnClose(() => { _booking = false; }).Class("md:w-96")
                .Heading("Book 12 pallets?").Text("They move into the warehouse today.")[
                    // Cancel closes the modal; Book runs the handler, busy while it waits.
                    Ui.DialogButtons.Save("Book").OnSave(async () =>
                    {
                        await Task.Delay(600);
                        _last = "booked 12 pallets";
                        _booking = false;
                    })
                ],
            Ui.Button.OnClick(() => { _merging = true; })["Merge suppliers"],
            Ui.Modal.Open(_merging).OnClose(() => { _merging = false; }).Class("md:w-96")[
                // A dialog that IS a form: Submit() makes Merge the form's submit — validated, busy, Enter sends it.
                Form.Model(_merger).OnSubmit(async m =>
                {
                    await Task.Delay(600);
                    _last = $"merged into {m.Supplier}";
                    _merging = false;
                }).Class("space-y-6")[
                    Ui.Heading.Level(2).Lg["Merge two suppliers"],
                    Ui.Input.Bind(() => _merger.Supplier).Label("Merged name"),
                    Ui.DialogButtons.Submit().Save("Merge")
                ]
            ],
            Ui.Text[$"Last action: {_last}."]
        ];

    private sealed class Merger
    {
        [Required]
        public string? Supplier { get; set; }
    }
}
