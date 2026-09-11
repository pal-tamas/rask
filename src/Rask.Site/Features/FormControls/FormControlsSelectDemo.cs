namespace Rask.Site.Features;

// UiSelect<T> — Rask.Core's Select<T> underneath — in both shapes side by side.
//   • Controlled — Value + OnChange: the parent owns the value in a field; OnChange writes it back and
//     re-renders this consumer, so the "Picked:" readout updates live (the controlled-OnChange fix).
//   • Bound — UiSelect.Bind(() => model.X): two-way binds the model property through the ambient EditContext.
// Both readouts refresh on every change with no StateHasChanged.
public sealed partial class FormControlsSelectDemo : Component
{
    private static readonly (string Value, string Text)[] Frameworks =
        [("Rask", "Rask"), ("Blazor", "Blazor"), ("htmx", "htmx")];

    private string _controlled = "Rask";
    private readonly Model _model = new();

    protected override Component? Render() =>
        Div.Class("grid grid-cols-12 gap-4")[
            Div.Class("col-span-12 md:col-span-6")[
                UiSelect
                    .Value(_controlled)
                    .Options(Frameworks)
                    .Label("Controlled (Value + OnChange)")
                    .OnChange(v => _controlled = v)
                    .Id("fc-select-controlled")
                    .Class("mb-2"),
                P.Class("text-sm text-ui-muted mb-0").Id("fc-select-controlled-out")[
                    "Picked: ", Strong[_controlled]
                ]
            ],
            Div.Class("col-span-12 md:col-span-6")[
                Form.Model(_model)[
                    UiSelect.Bind(() => _model.Framework).Options(Frameworks).Label("Bound (two-way)")
                        .Id("fc-select-bound").Class("mb-2")
                ],
                P.Class("text-sm text-ui-muted mb-0").Id("fc-select-bound-out")[
                    "Picked: ", Strong[_model.Framework]
                ]
            ]
        ];

    private sealed class Model
    {
        public string Framework { get; set; } = "Rask";
    }
}
