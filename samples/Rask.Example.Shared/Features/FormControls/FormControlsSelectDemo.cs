namespace Rask.Example.Shared.Features;

// Select<T> in both shapes side by side.
//   • Controlled — Value + OnChange: the parent owns the value in a field; OnChange writes it back and
//     re-renders this consumer, so the "Picked:" readout updates live (the controlled-OnChange fix).
//   • Bound — Select.Bind(() => model.X): two-way binds the model property through the ambient EditContext.
// Both readouts refresh on every change with no StateHasChanged.
public sealed partial class FormControlsSelectDemo : Component
{
    private string _controlled = "Rask";
    private readonly Model _model = new();

    protected override Component? Render() =>
        Div.Class("grid grid-cols-12 gap-4")[
            Div.Class("md:col-span-6")[
                Label.Class($"{Ui.Label} font-semibold")["Controlled (Value + OnChange)"],
                Select
                    .Value(_controlled)
                    .OnChange(v => _controlled = v)
                    .Class($"{Ui.Select} mb-2")
                    .Id("fc-select-controlled")[
                    Option.Value("Rask"), Option.Value("Blazor"), Option.Value("htmx")
                ],
                P.Class("text-sm text-slate-500 dark:text-slate-400 mb-0").Id("fc-select-controlled-out")[
                    "Picked: ", Strong[_controlled]
                ]
            ],
            Div.Class("md:col-span-6")[
                Label.Class($"{Ui.Label} font-semibold")["Bound (two-way)"],
                Form.Model(_model)[
                    Select.Bind(() => _model.Framework).Class($"{Ui.Select} mb-2").Id("fc-select-bound")[
                        Option.Value("Rask"), Option.Value("Blazor"), Option.Value("htmx")
                    ]
                ],
                P.Class("text-sm text-slate-500 dark:text-slate-400 mb-0").Id("fc-select-bound-out")[
                    "Picked: ", Strong[_model.Framework]
                ]
            ]
        ];

    private sealed class Model
    {
        public string Framework { get; set; } = "Rask";
    }
}
