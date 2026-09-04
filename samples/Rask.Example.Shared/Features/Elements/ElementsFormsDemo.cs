namespace Rask.Example.Shared.Features;

// Form-associated elements: form, fieldset/legend, label, input, select/optgroup/option, textarea,
// datalist, output, progress, meter, button. (See the Forms page for binding/validation.)
public sealed partial class ElementsFormsDemo : Component
{
    // The elements below are plain HTML; `Form` binds a model, so this one holds their fields.
    private readonly Fields _fields = new();

    protected override Component? Render() => Form.Model(_fields).Class("flex flex-col gap-3")[
        Fieldset.Class("border rounded p-3")[
            Legend.Class("text-base float-none w-auto px-2")["Profile"],
            Div.Class("mb-2")[
                Label.For("nm").Class($"{Tw.Label} text-sm mb-1")["Name"],
                Input.Value<string>(null)
                    .Type(InputType.Text)
                    .Id("nm")
                    .Class(Tw.Input)
                    .Placeholder("Jane Doe")
                    .List("suggestions"),
                Datalist.Id("suggestions")[Option.Value("Jane Doe"), Option.Value("Ada Lovelace")]
            ],
            Div.Class("mb-2")[
                Label.For("fruit").Class($"{Tw.Label} text-sm mb-1")["Favourite"],
                Select.Value<string>(null).Id("fruit").Name("fruit").Class(Tw.Select)[
                    Optgroup.Label("Fruit")[Option.Value("apple")["Apple"], Option.Value("pear").Selected(true)["Pear"]],
                    Optgroup.Label("Veg")[Option.Value("kale")["Kale"]]
                ]
            ],
            Div.Class("mb-0")[
                Label.For("bio").Class($"{Tw.Label} text-sm mb-1")["Bio"],
                Textarea.Value<string>(null).Id("bio").Class(Tw.Input).Placeholder("About you…")
            ]
        ],
        Div.Class("grid grid-cols-12 gap-4 items-center")[
            Div.Class("col-auto")[
                Label.Class($"{Tw.Label} text-sm mb-1")["Progress"], Br,
                Progress.Value(0.6).Max(1.0)
            ],
            Div.Class("col-auto")[
                Label.Class($"{Tw.Label} text-sm mb-1")["Meter"], Br,
                Meter.Value(0.8).Min(0).Max(1).Low(0.2).High(0.9).Optimum(1)
            ],
            Div.Class("col-auto")[
                Label.Class($"{Tw.Label} text-sm mb-1")["Output"], Br,
                Output.For("fruit")["Pear"]
            ]
        ],
        Div[
            Button.Class(Tw.BtnPrimary).Type("submit")["Submit"], " ",
            Button.Class(Tw.BtnOutlineSecondary).Type("reset")["Reset"]
        ]
    ];

    private sealed class Fields
    {
        public string? Name { get; set; }
    }
}
