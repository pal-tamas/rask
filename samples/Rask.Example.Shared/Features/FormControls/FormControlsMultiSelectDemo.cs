namespace Rask.Example.Shared.Features;

// MultiSelect<TItem> (example control: a custom dropdown of checkable chips) in both shapes side by side.
//   • Controlled — Options + Value + OnChange: the parent owns the selection; OnChange (auto-wrapped) hands
//     back a fresh collection and re-renders this consumer so the "Selected:" readout updates.
//   • Bound — MultiSelect(() => model.X, options): two-way binds the model collection through the EditContext.
public sealed class FormControlsMultiSelectDemo : Component
{
    private static readonly string[] AllTopics = ["News", "Sports", "Tech", "Music", "Travel"];

    private ICollection<string> _controlled = [];
    private readonly Model _model = new();

    protected override RenderResult Render() =>
        Div(Class: "row g-4")[
            Div(Class: "col-md-6")[
                Label(Class: "form-label fw-semibold d-block")["Controlled (Value + OnChange)"],
                MultiSelect<string>(
                    AllTopics,
                    Value: _controlled.ToList(),
                    OnChange: next => _controlled = next,
                    Id: "fc-multiselect-controlled",
                    Placeholder: "Choose topics…"),
                P(Class: "small text-secondary mb-0 mt-2", Id: "fc-multiselect-controlled-out")[
                    "Selected: ", Strong()[_controlled.Count == 0 ? "none" : string.Join(", ", _controlled)]
                ]
            ],
            Div(Class: "col-md-6")[
                Label(Class: "form-label fw-semibold d-block")["Bound (two-way)"],
                Form(_model)[
                    MultiSelect(() => _model.Topics, AllTopics, Id: "fc-multiselect-bound",
                        Placeholder: "Choose topics…")
                ],
                P(Class: "small text-secondary mb-0 mt-2", Id: "fc-multiselect-bound-out")[
                    "Selected: ",
                    Strong()[_model.Topics.Count == 0 ? "none" : string.Join(", ", _model.Topics)]
                ]
            ]
        ];

    private sealed class Model
    {
        public List<string> Topics { get; } = [];
    }
}
