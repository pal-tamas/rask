namespace Rask.Site.Features;

// A form that asks before it is left unsaved. `ConfirmLeave` is the whole of it: from the first character
// typed, a link to another page, the Back button and closing the tab all ask first, and staying keeps the
// page exactly as it was. Saving ends it — until the next edit.
public sealed partial class FormConfirmLeaveDemo : Component
{
    private readonly Model _model = new();
    private string _saved = "";

    protected override Component? Render() =>
        Div.Class("grid grid-cols-12 gap-4")[
            Div.Class("col-span-12 md:col-span-7")[
                Form.Model(_model).OnSubmit(Save)
                    .ConfirmLeave("Leave without saving your note?").Id("fcl-form")[
                    Ui.Input.Bind(() => _model.Note).Label("Note").Id("fcl-input").Class("mb-2"),
                    Ui.Button.Primary.Submit.Id("fcl-submit")["Save"]
                ]
            ],
            Div.Class("col-span-12 md:col-span-5")[
                P.Class("text-sm text-slate-500 dark:text-slate-400 mb-2").Id("fcl-out")[
                    "Saved: ", Strong[_saved.Length == 0 ? "(nothing yet)" : _saved]
                ],
                P.Class("text-sm text-slate-500 dark:text-slate-400 mb-0")[
                    "Type a note, then follow any link in the sidebar or press Back."
                ]
            ]
        ];

    private void Save(Model m) => _saved = m.Note;

    private sealed class Model
    {
        public string Note { get; set; } = "";
    }
}
