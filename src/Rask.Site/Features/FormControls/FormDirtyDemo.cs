using Rask.Core.Forms;

namespace Rask.Site.Features;

// A form that shows what the reader changed, as they change it. The browser writes `data-dirty` on the <form> and
// on each field whose value is not the one it had — no round trip, nothing to turn on — and `data-dirty:` styles
// it. `f.Unsaved` is the server's word for the same thing, for what the page decides; a save clears both, and
// "Start again" (EditContext.Reset) puts every field back as it was first drawn.
public sealed partial class FormDirtyDemo : Component
{
    private readonly Model _model = new();
    private readonly EditContext _form;
    private string _saved = "";

    public FormDirtyDemo() => _form = new EditContext(_model);

    protected override Component? Render() =>
        Div.Class("grid grid-cols-12 gap-4")[
            Div.Class("col-span-12 md:col-span-7")[
                Form.Model(_model).Context(_form).OnSubmit(Save).Id("fd-form").Class("group/form")[f => [
                    Ui.Input.Bind(() => _model.Tank).Label("Tank").Id("fd-tank")
                        .Class("mb-2 data-dirty:border-amber-500 dark:data-dirty:border-amber-400"),
                    Ui.Input.Bind(() => _model.Litres).Label("Litres").Id("fd-litres")
                        .Class("mb-2 data-dirty:border-amber-500 dark:data-dirty:border-amber-400"),
                    Ui.Checkbox.Bind(() => _model.Urgent).Label("Urgent").Id("fd-urgent").Class("mb-3"),
                    Div.Class("flex items-center gap-2")[
                        Ui.Button.Primary.Submit.Disabled(f.Submitting).Id("fd-save")[f.Submitting ? "Saving…" : "Save"],
                        Ui.Button.Ghost.OnClick(() => _form.Reset()).Id("fd-reset")["Start again"],
                        Span.Class("hidden text-sm text-amber-600 group-data-dirty/form:inline dark:text-amber-400").Id("fd-marked")[
                            "Changed"
                        ]
                    ],
                    P.Class("mt-2 text-sm text-slate-500 dark:text-slate-400 mb-0").Id("fd-unsaved")[
                        f.Unsaved ? "The server has unsaved values." : "The server holds nothing unsaved."
                    ]
                ]]
            ],
            Div.Class("col-span-12 md:col-span-5")[
                P.Class("text-sm text-slate-500 dark:text-slate-400 mb-0").Id("fd-out")[
                    "Saved: ", Strong[_saved.Length == 0 ? "(nothing yet)" : _saved]
                ]
            ]
        ];

    private async Task Save(Model m)
    {
        await Task.Delay(300).ConfigureAwait(false);
        _saved = $"{m.Tank}, {m.Litres} l{(m.Urgent ? ", urgent" : "")}";
    }

    private sealed class Model
    {
        public string Tank { get; set; } = "North";

        public int Litres { get; set; } = 40;

        public bool Urgent { get; set; }
    }
}
