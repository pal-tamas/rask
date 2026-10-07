using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary><c>fluxui.dev/components/select</c>, example by example.</summary>
/// <remarks>
///     <para>
///     Eighteen examples are rendered on Flux's page; the sections between them (the button slot, clearable,
///     the search slot, selected suffix, checkbox indicator, clearing search, the input slot, backend search)
///     show code only. The tool names an example by the last <c>&lt;h2&gt;</c> above it, which is why six of
///     the listbox examples sit under "clearable" and the last two under "loading-message".
///     </para>
///     <para>
///     The words and the picked options are the rendered page's: its listbox examples open on an answer where
///     the markdown shows none. <c>scripts/flux/parity-select.mjs</c> opens every drawn list here and on
///     Flux's page and compares what is then on screen.
///     </para>
/// </remarks>
public sealed partial class SelectParity : FluxParity
{
    // The column Flux's docs page sets every select example in, and the select's own width inside it.
    private const string Column = "display:flex;justify-content:center;max-width:384px;margin:0 auto";

    private const string Narrow = "width:100%;max-width:256px";

    // What an app's own Tailwind build emits for the classes the width example hands to Class and OptionsClass.
    private const string AppUtilities = "<style>.min-w-32{min-width:8rem}.min-w-72{min-width:18rem}</style>";

    // The swatch in "With custom content": rounded-full size-4 bg-red-500. The page's own examples lighten most
    // of the hues in dark, which its markdown leaves out.
    private const string Swatches =
        "<style>.parity-swatch{border-radius:calc(infinity * 1px);width:16px;height:16px;background:var(--light)}"
        + "@media (prefers-color-scheme:dark){.parity-swatch{background:var(--dark)}}</style>";

    private static string DarkShade(string name) => name is "Red" or "Lime" or "Green" or "Rose" ? "500" : "400";

    private static readonly string[] Industries =
        ["Photography", "Design services", "Web development", "Accounting", "Legal services", "Consulting", "Other"];

    private static readonly (int Id, string Name)[] Projects =
        [(1, "Branding"), (2, "Analytics"), (3, "Infrastructure"), (4, "Documentation"), (5, "Security"), (6, "Performance")];

    private static readonly string[] Colors =
    [
        "Red", "Orange", "Amber", "Yellow", "Lime", "Green", "Emerald", "Teal", "Cyan", "Sky", "Blue", "Indigo",
        "Violet", "Purple", "Fuchsia", "Pink", "Rose",
    ];

    public override string Page => "select";

    public override IEnumerable<(string Section, Component Example)> Examples()
    {
        yield return ("", Example(Ui.Select.Of<string>().Placeholder("Choose industry...")[IndustryOptions()]));

        yield return ("small", Example(Ui.Select.Of<string>().Sm.Placeholder("Choose industry...")[IndustryOptions()]));

        yield return ("option-groups", Example(Ui.Select.Of<string>().Placeholder("Choose an industry...")[
            Ui.SelectGroup.Label("Creative")[
                Ui.SelectOption.Value("photography")["Photography"],
                Ui.SelectOption.Value("design")["Design services"]
            ],
            Ui.SelectGroup.Label("Technology")[
                Ui.SelectOption.Value("web-development")["Web development"],
                Ui.SelectOption.Value("it-consulting")["IT consulting"]
            ],
            Ui.SelectGroup.Label("Services")[
                Ui.SelectOption.Value("accounting")["Accounting"],
                Ui.SelectOption.Value("legal")["Legal services"],
                Ui.SelectOption.Value("consulting")["Consulting"]
            ]
        ]));

        yield return ("custom-select", Example(Ui.Select.Of<string>().Listbox.Placeholder("Choose industry...")[IndustryOptions()]));

        foreach (var example in Listboxes())
        {
            yield return ("clearable", example);
        }

        yield return ("searchable-select", Example(
            Ui.Select.Of<string>().Listbox.Searchable().Placeholder("Choose industries...")[IndustryOptions()]));

        yield return ("the-search-slot", Example(Ui.Select.Of<string>().Listbox.Searchable().Placeholder("Choose category...")[
            Ui.SelectOption.Value("fruit").Keywords("apple orange pear")["Fruit"],
            Ui.SelectOption.Value("vegetables").Keywords("carrot broccoli spinach")["Vegetables"],
            Ui.SelectOption.Value("drinks").Keywords("coffee tea juice")["Drinks"]
        ]));

        yield return ("multiple-select", Example(
            Ui.Select.Values<string>([]).Listbox.Multiple().Placeholder("Choose industries...")[IndustryOptions()]));

        yield return ("combobox", Example(Ui.Select.Of<string>().Combobox.Placeholder("Choose industry...")[IndustryOptions()]));

        // wire:click="createProject" is OnClick; `Create "<span wire:text="search">"` are the row's own words.
        yield return ("create-option", Example(CreateCombobox(filter: true)));
        yield return ("create-option", Example(CreateCombobox(filter: false)));

        yield return ("loading-message", Example(CreateCombobox(filter: false)));

        // Flux's `modal="create-project"` opens a modal by its name through Flux's script; here the row's OnClick
        // is the page's to answer. The modal beside it is another page's component and is not drawn.
        yield return ("loading-message", Example(Ui.Select.Of<int?>().Listbox.Placeholder("Start typing...")[
            ProjectOptions(),
            Ui.SelectOptionCreate["Create new"]
        ]));
    }

    // The six listbox examples under "The button slot" … "Customizing the dropdown width".
    private static IEnumerable<Component> Listboxes()
    {
        yield return Example(Ui.Select.Value("last-month").Listbox.Prefix("Compare to")[
            Ui.SelectOption.Value("previous-period")["Previous period"],
            Ui.SelectOption.Value("last-month")["Last month"],
            Ui.SelectOption.Value("last-quarter")["Last quarter"],
            Ui.SelectOption.Value("last-year")["Last year"]
        ]);

        yield return Example(Ui.Select.Value("card").Listbox.Placeholder("Choose method...")[
            Ui.SelectOption.Value("card").Label("Credit card").Icon(Ui.IconName.CreditCard),
            Ui.SelectOption.Value("paypal").Label("PayPal").Icon(Ui.IconName.Banknotes),
            Ui.SelectOption.Value("bank").Label("Bank transfer").Icon(Ui.IconName.BuildingLibrary)
        ]);

        yield return Example(Ui.Select.Value("basic").Listbox.Placeholder("Choose plan...")[
            Ui.SelectOption.Value("basic").Label("Basic").Description("For individuals getting started"),
            Ui.SelectOption.Value("pro").Label("Pro").Description("For small teams that need more power"),
            Ui.SelectOption.Value("enterprise").Label("Enterprise").Description("Advanced controls and support")
        ]);

        yield return Example(Ui.Select.Value("calebporzio").Listbox.Placeholder("Search people...")[
            Ui.SelectOption.Value("calebporzio").Label("Caleb Porzio").Avatar("https://unavatar.io/github/calebporzio"),
            Ui.SelectOption.Value("hugosaintemarie").Label("Hugo Sainte-Marie").Avatar("https://unavatar.io/github/hugosaintemarie"),
            Ui.SelectOption.Value("joshhanley").Label("Josh Hanley").Avatar("https://unavatar.io/github/joshhanley"),
            Ui.SelectOption.Value("jasonlbeggs").Label("Jason Beggs").Avatar("https://unavatar.io/github/jasonlbeggs")
        ]);

        yield return Div.Style(Column)[
            Raw.Value(Swatches),
            Div.Style(Narrow)[Ui.Select.Value("red").Listbox.Placeholder("Select color...")[Colors.Select(ColorOption)]]
        ];

        yield return Div.Style(Column)[
            Raw.Value(AppUtilities),
            // The markdown writes class="max-w-32"; the rendered example is min-w-32, in a box as wide as its content.
            Div[
                Ui.Select.Value("public").Listbox.Placeholder("Select visibility...").Class("min-w-32").OptionsClass("min-w-72")[
                    Ui.SelectOption.Value("public").Label("Public").Icon(Ui.IconName.GlobeAlt)
                        .Description("Shown on your calendar and eligible to be featured."),
                    Ui.SelectOption.Value("private").Label("Private").Icon(Ui.IconName.LockClosed)
                        .Description("Unlisted. Only people invited with the link can register.")
                ]
            ]
        ];
    }

    private static Component Example(Component select) => Div.Style(Column)[Div.Style(Narrow)[select]];

    private static IEnumerable<Component> IndustryOptions() => Industries.Select(name => (Component)Ui.SelectOption.Key(name)[name]);

    private static IEnumerable<Component> ProjectOptions() =>
        Projects.Select(project => (Component)Ui.SelectOption.Key(project.Id).Value(project.Id)[project.Name]);

    private static Component CreateCombobox(bool filter) =>
        Ui.Select.Of<int?>().Combobox.Filter(filter)[
            Ui.SelectInput.Placeholder("Start typing..."),
            ProjectOptions(),
            Ui.SelectOptionCreate.MinLength(2)["Create \"", Span, "\""]
        ];

    // <div class="flex items-center gap-2"><div class="rounded-full size-4 bg-red-500"></div> Red</div>: the
    // swatch is the example's own markup, so its classes are stated here rather than taken from the kit's sheet.
    private static Component ColorOption(string name) =>
        Ui.SelectOption.Key(name).Value(name.ToLowerInvariant())[
            Div.Style("display:flex;align-items:center;gap:8px")[
                Div.Class("parity-swatch").Style($"--light:var(--color-{name.ToLowerInvariant()}-500);--dark:var(--color-{name.ToLowerInvariant()}-{DarkShade(name)})"),
                name
            ]
        ];
}
