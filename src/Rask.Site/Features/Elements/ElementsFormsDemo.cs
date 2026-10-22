namespace Rask.Site.Features;

// Form-associated elements: form, fieldset/legend, label, input, select/optgroup/option, textarea,
// datalist, output, progress, meter, button. (See the Forms page for binding/validation.)
//
// The raw tags ARE the subject here, so they stay raw — drawn with the utilities Flux's input, label and
// button are drawn with, rather than the kit's field components, which the Forms page shows.
public sealed partial class ElementsFormsDemo : Component
{
    // The elements below are plain HTML; `Form` binds a model, so this one holds their fields.
    private readonly Fields _fields = new();

    // Flux's input, select and textarea, as plain utilities on the raw tags.
    internal const string Field =
        "block w-full h-10 rounded-lg border border-zinc-200 border-b-zinc-300/80 bg-white px-3 text-base text-zinc-700 "
        + "shadow-xs placeholder-zinc-400 sm:text-sm dark:border-white/10 dark:bg-white/10 dark:text-zinc-300";

    private const string Area =
        "block w-full rounded-lg border border-zinc-200 border-b-zinc-300/80 bg-white p-3 text-base text-zinc-700 "
        + "shadow-xs placeholder-zinc-400 sm:text-sm dark:border-white/10 dark:bg-white/10 dark:text-zinc-300";

    private const string Caption = "mb-1 inline-block text-sm font-medium text-zinc-800 dark:text-white";

    private const string ResetButton =
        "inline-flex h-10 items-center rounded-lg border border-zinc-200 border-b-zinc-300/80 bg-white px-4 text-sm "
        + "font-medium text-zinc-800 shadow-xs hover:bg-zinc-50 dark:border-zinc-600 dark:bg-zinc-700 dark:text-white "
        + "dark:hover:bg-zinc-600/75";

    protected override Component? Render() => Form.Model(_fields).Class("flex flex-col gap-3")[
        Fieldset.Class("border rounded p-3")[
            Legend.Class("text-base float-none w-auto px-2")["Profile"],
            Div.Class("mb-2")[
                Label.For("nm").Class(Caption)["Name"],
                Input.Of<string>()
                    .Type(InputType.Text)
                    .Id("nm")
                    .Class(Field)
                    .Placeholder("Jane Doe")
                    .List("suggestions"),
                Datalist.Id("suggestions")[Option.Value("Jane Doe"), Option.Value("Ada Lovelace")]
            ],
            Div.Class("mb-2")[
                Label.For("fruit").Class(Caption)["Favourite"],
                Select.Of<string>().Id("fruit").Name("fruit").Class(Field)[
                    Optgroup.Label("Fruit")[Option.Value("apple")["Apple"], Option.Value("pear").Selected()["Pear"]],
                    Optgroup.Label("Veg")[Option.Value("kale")["Kale"]]
                ]
            ],
            Div.Class("mb-0")[
                Label.For("bio").Class(Caption)["Bio"],
                Textarea.Of<string>().Id("bio").Class(Area).Placeholder("About you…")
            ]
        ],
        Div.Class("grid grid-cols-12 gap-4 items-center")[
            Div.Class("col-auto")[
                Label.Class(Caption)["Progress"], Br,
                Progress.Value(0.6).Max(1.0)
            ],
            Div.Class("col-auto")[
                Label.Class(Caption)["Meter"], Br,
                Meter.Value(0.8).Min(0).Max(1).Low(0.2).High(0.9).Optimum(1)
            ],
            Div.Class("col-auto")[
                Label.Class(Caption)["Output"], Br,
                Output.For("fruit")["Pear"]
            ]
        ],
        Div[
            Ui.Button.Primary.Submit["Submit"], " ",
            Button.Type(ButtonType.Reset).Class(ResetButton)["Reset"]
        ]
    ];

    public sealed class Fields
    {
        public string? Name { get; set; }
    }
}
