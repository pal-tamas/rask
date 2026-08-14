namespace Rask.Example.Shared.Features;

// BsCheckboxGroup<TItem> (example control, collection) in both shapes side by side.
//   • Controlled — Options + Value + OnChange: the parent owns the selection; OnChange (auto-wrapped)
//     hands back a fresh collection and re-renders this consumer so the readout updates.
//   • Bound — BsCheckboxGroup.Bind(() => model.X, options): two-way binds the model collection.
public sealed partial class FormControlsCheckboxDemo : Component
{
    private static readonly string[] AllInterests = ["Web", "Mobile", "AI", "Games"];

    private ICollection<string> _controlled = [];
    private readonly Model _model = new();

    protected override Component? Render() =>
        BsRow.Gutter(4)[
            BsCol.Md(6).Id("fc-checkbox-controlled")[
                Label.Class("form-label fw-semibold d-block")["Controlled (Value + OnChange)"],
                BsCheckboxGroup
                    .Value(_controlled.ToList())
                    .Options(AllInterests)
                    .OnChange(next => _controlled = next)
                    .Name("fc-checkbox-c")
                    .ItemClass("form-check-inline"),
                P.Class("small text-secondary mb-0").Id("fc-checkbox-controlled-out")[
                    "Interests: ", Strong[_controlled.Count == 0 ? "none" : string.Join(", ", _controlled)]
                ]
            ],
            BsCol.Md(6).Id("fc-checkbox-bound")[
                Label.Class("form-label fw-semibold d-block")["Bound (two-way)"],
                Form.Model(_model)[
                    // Label: names the group — the options render inside a <fieldset>/<legend> for the
                    // correct accessible grouping semantics.
                    BsCheckboxGroup.Bind(() => _model.Interests)
                        .Options(AllInterests)
                        .Name("fc-checkbox-b")
                        .Label("Interests")
                        .ItemClass("form-check-inline")
                ],
                P.Class("small text-secondary mb-0").Id("fc-checkbox-bound-out")[
                    "Interests: ",
                    Strong[_model.Interests.Count == 0 ? "none" : string.Join(", ", _model.Interests)]
                ]
            ]
        ];

    private sealed class Model
    {
        public List<string> Interests { get; } = [];
    }
}
