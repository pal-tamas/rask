namespace Rask.Example.Shared.Features;

// Form-associated elements: form, fieldset/legend, label, input, select/optgroup/option, textarea,
// datalist, output, progress, meter, button. (See the Forms page for binding/validation.)
public sealed class ElementsFormsDemo : Component
{
    protected override RenderResult Render() => Form(Class: "vstack gap-3")[
        Fieldset(Class: "border rounded p-3")[
            Legend(Class: "fs-6 float-none w-auto px-2")["Profile"],
            Div(Class: "mb-2")[
                Label("nm", Class: "form-label small mb-1")["Name"],
                Input<string>(InputType.Text, Id: "nm", Class: "form-control form-control-sm",
                    Placeholder: "Jane Doe", List: "suggestions"),
                Datalist(Id: "suggestions")[Option(Value: "Jane Doe"), Option(Value: "Ada Lovelace")]
            ],
            Div(Class: "mb-2")[
                Label("fruit", Class: "form-label small mb-1")["Favourite"],
                Select<string>(Id: "fruit", Name: "fruit", Class: "form-select form-select-sm")[
                    Optgroup(Label: "Fruit")[Option(Value: "apple")["Apple"], Option(Value: "pear", Selected: true)["Pear"]],
                    Optgroup(Label: "Veg")[Option(Value: "kale")["Kale"]]
                ]
            ],
            Div(Class: "mb-0")[
                Label("bio", Class: "form-label small mb-1")["Bio"],
                Textarea<string>(Id: "bio", Class: "form-control form-control-sm", Placeholder: "About you…")
            ]
        ],
        Div(Class: "row align-items-center g-3")[
            Div(Class: "col-auto")[
                Label(Class: "form-label small mb-1")["Progress"], Br(),
                Progress(Value: 0.6, Max: 1.0)
            ],
            Div(Class: "col-auto")[
                Label(Class: "form-label small mb-1")["Meter"], Br(),
                Meter(Value: 0.8, Min: 0, Max: 1, Low: 0.2, High: 0.9, Optimum: 1)
            ],
            Div(Class: "col-auto")[
                Label(Class: "form-label small mb-1")["Output"], Br(),
                Output(For: "fruit")["Pear"]
            ]
        ],
        Div()[
            Button("submit", Class: "btn btn-primary btn-sm")["Submit"], " ",
            Button("reset", Class: "btn btn-outline-secondary btn-sm")["Reset"]
        ]
    ];
}
