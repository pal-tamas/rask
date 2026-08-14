namespace Rask.Example.Shared.Features;

// Form-associated elements: form, fieldset/legend, label, input, select/optgroup/option, textarea,
// datalist, output, progress, meter, button. (See the Forms page for binding/validation.)
public sealed partial class ElementsFormsDemo : Component
{
    // The elements below are plain HTML; `Form` binds a model, so this one holds their fields.
    private readonly Fields _fields = new();

    protected override Component? Render() => Form.Model(_fields).Class("vstack gap-3")[
        Fieldset.Class("border rounded p-3")[
            Legend.Class("fs-6 float-none w-auto px-2")["Profile"],
            Div.Class("mb-2")[
                Label.For("nm").Class("form-label small mb-1")["Name"],
                Input.Value<string>(null)
                    .Type(InputType.Text)
                    .Id("nm")
                    .Class("form-control form-control-sm")
                    .Placeholder("Jane Doe")
                    .List("suggestions"),
                Datalist.Id("suggestions")[Option.Value("Jane Doe"), Option.Value("Ada Lovelace")]
            ],
            Div.Class("mb-2")[
                Label.For("fruit").Class("form-label small mb-1")["Favourite"],
                Select.Value<string>(null).Id("fruit").Name("fruit").Class("form-select form-select-sm")[
                    Optgroup.Label("Fruit")[Option.Value("apple")["Apple"], Option.Value("pear").Selected(true)["Pear"]],
                    Optgroup.Label("Veg")[Option.Value("kale")["Kale"]]
                ]
            ],
            Div.Class("mb-0")[
                Label.For("bio").Class("form-label small mb-1")["Bio"],
                Textarea.Value<string>(null).Id("bio").Class("form-control form-control-sm").Placeholder("About you…")
            ]
        ],
        BsRow.Gutter(3).Class(Flex.Align(BsAlign.Center))[
            BsCol.Auto(true)[
                Label.Class("form-label small mb-1")["Progress"], Br,
                Progress.Value(0.6).Max(1.0)
            ],
            BsCol.Auto(true)[
                Label.Class("form-label small mb-1")["Meter"], Br,
                Meter.Value(0.8).Min(0).Max(1).Low(0.2).High(0.9).Optimum(1)
            ],
            BsCol.Auto(true)[
                Label.Class("form-label small mb-1")["Output"], Br,
                Output.For("fruit")["Pear"]
            ]
        ],
        Div[
            BsButton.Type("submit").Color(BsColor.Primary).Size(BsSize.Sm)["Submit"], " ",
            BsButton.Type("reset").Color(BsColor.Secondary).Outline(true).Size(BsSize.Sm)["Reset"]
        ]
    ];

    private sealed class Fields
    {
        public string? Name { get; set; }
    }
}
