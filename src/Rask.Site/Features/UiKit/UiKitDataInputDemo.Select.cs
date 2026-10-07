namespace Rask.Site.Features.UiKit;

// Flux UI's select page, example by example: https://fluxui.dev/components/select
public sealed partial class UiKitDataInputDemo
{
    private static readonly string[] Industries =
        ["Photography", "Design services", "Web development", "Accounting", "Legal services", "Consulting", "Other"];

    private static readonly string[] Swatches = ["red", "orange", "amber", "yellow", "lime", "green", "emerald", "teal"];

    private readonly List<(int Id, string Name)> _projects =
        [(1, "Branding"), (2, "Analytics"), (3, "Infrastructure"), (4, "Documentation"), (5, "Security"), (6, "Performance")];

    private string? _industry;
    private string? _grouped;
    private string? _listed;
    private string _comparison = "last-month";
    private string? _method = "card";
    private string? _tier = "basic";
    private string? _person = "calebporzio";
    private string? _colour = "red";
    private string? _visibility = "public";
    private string? _searched;
    private string? _category;
    private List<string> _industries = [];
    private string? _typed;
    private int? _project;
    private int? _found;
    private string _projectSearch = "";

    private Component SelectSection() =>
        Section(
            "Select",
            "Choose a single option from a dropdown list. The default is the browser's own select; Sm is the smaller "
            + "one, and Ui.SelectGroup organises related options under a label.",
            Div.Data(Testid("ui-select")).Class("grid max-w-3xl gap-6 sm:grid-cols-3")[
                Ui.Select.Key("native").Value(_industry).OnChange(picked => _industry = picked).Placeholder("Choose industry...").Id("ui-select-native")[
                    Industries.Select(name => Ui.SelectOption.Key(name)[name])
                ],
                Ui.Select.Key("small").Value(_industry).OnChange(picked => _industry = picked).Sm.Placeholder("Choose industry...")[
                    Industries.Select(name => Ui.SelectOption.Key(name)[name])
                ],
                Ui.Select.Key("groups").Value(_grouped).OnChange(picked => _grouped = picked).Placeholder("Choose an industry...")[
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
                ],
                P.Class("text-sm text-ui-muted sm:col-span-3").Data(Testid("ui-select-state"))[
                    _industry is null ? "Nothing chosen." : $"Chosen: {_industry}."
                ]
            ]);

    private Component ListboxSection() =>
        Section(
            "Custom select",
            "Listbox draws the list instead of handing it to the browser, for options that carry more than words: a "
            + "prefix that stays in the button, icons, descriptions, avatars, content of your own, a list wider than "
            + "its button, and Clearable for a way back to nothing chosen.",
            Div.Data(Testid("ui-listbox")).Class("grid max-w-3xl items-start gap-6 sm:grid-cols-3")[
                Ui.Select.Key("listbox").Value(_listed).OnChange(picked => _listed = picked).Listbox.Clearable().Placeholder("Choose industry...").Id("ui-select-listbox")[
                    Industries.Select(name => Ui.SelectOption.Key(name)[name])
                ],
                Ui.Select.Key("prefix").Value(_comparison).OnChange(picked => _comparison = picked).Listbox.Prefix("Compare to")[
                    Ui.SelectOption.Value("previous-period")["Previous period"],
                    Ui.SelectOption.Value("last-month")["Last month"],
                    Ui.SelectOption.Value("last-quarter")["Last quarter"],
                    Ui.SelectOption.Value("last-year")["Last year"]
                ],
                Ui.Select.Key("icons").Value(_method).OnChange(picked => _method = picked).Listbox.Placeholder("Choose method...")[
                    Ui.SelectOption.Value("card").Label("Credit card").Icon(Ui.IconName.CreditCard),
                    Ui.SelectOption.Value("paypal").Label("PayPal").Icon(Ui.IconName.Banknotes),
                    Ui.SelectOption.Value("bank").Label("Bank transfer").Icon(Ui.IconName.BuildingLibrary)
                ],
                Ui.Select.Key("descriptions").Value(_tier).OnChange(picked => _tier = picked).Listbox.Placeholder("Choose plan...")[
                    Ui.SelectOption.Value("basic").Label("Basic").Description("For individuals getting started"),
                    Ui.SelectOption.Value("pro").Label("Pro").Description("For small teams that need more power"),
                    Ui.SelectOption.Value("enterprise").Label("Enterprise").Description("Advanced controls and support")
                ],
                Ui.Select.Key("avatars").Value(_person).OnChange(picked => _person = picked).Listbox.Placeholder("Search people...")[
                    Ui.SelectOption.Value("calebporzio").Label("Caleb Porzio").Avatar("https://unavatar.io/github/calebporzio"),
                    Ui.SelectOption.Value("taylorotwell").Label("Taylor Otwell").Avatar("https://unavatar.io/github/taylorotwell"),
                    Ui.SelectOption.Value("adamwathan").Label("Adam Wathan").Avatar("https://unavatar.io/github/adamwathan")
                ],
                Ui.Select.Key("content").Value(_colour).OnChange(picked => _colour = picked).Listbox.Placeholder("Select color...")[
                    Swatches.Select(Swatch)
                ],
                Ui.Select.Key("width").Value(_visibility).OnChange(picked => _visibility = picked).Listbox.Placeholder("Select visibility...").Class("max-w-32")
                    .OptionsClass("min-w-72")[
                    Ui.SelectOption.Value("public").Label("Public").Icon(Ui.IconName.GlobeAlt)
                        .Description("Shown on your calendar and eligible to be featured on our homepage."),
                    Ui.SelectOption.Value("private").Label("Private").Icon(Ui.IconName.LockClosed)
                        .Description("Unlisted. Only invited people and people with the link can register.")
                ],
                P.Class("text-sm text-ui-muted sm:col-span-3").Data(Testid("ui-listbox-state"))[
                    _listed is null ? "Nothing chosen." : $"Chosen: {_listed}."
                ]
            ]);

    private Component SearchableSection() =>
        Section(
            "Searchable select",
            "Searchable puts a search field over the listbox's options, matching whatever the case or the accents; "
            + "Keywords on an option are more words to find it by — \"apple\" finds Fruit. Multiple picks several "
            + "into a collection and leaves the list open while you do.",
            Div.Data(Testid("ui-select-search")).Class("grid max-w-3xl items-start gap-6 sm:grid-cols-3")[
                Ui.Select.Key("search").Value(_searched).OnChange(picked => _searched = picked).Listbox.Searchable().Placeholder("Choose industries...").Id("ui-select-searchable")[
                    Industries.Select(name => Ui.SelectOption.Key(name)[name])
                ],
                Ui.Select.Key("keywords").Value(_category).OnChange(picked => _category = picked).Listbox.Searchable().Placeholder("Choose category...")[
                    Ui.SelectOption.Value("fruit").Keywords("apple orange pear")["Fruit"],
                    Ui.SelectOption.Value("vegetables").Keywords("carrot broccoli spinach")["Vegetables"],
                    Ui.SelectOption.Value("drinks").Keywords("coffee tea juice")["Drinks"]
                ],
                Div.Data(Testid("ui-multiselect"))[
                    Ui.Select.Key("multiple").Values(_industries).OnChange(picked => _industries = [.. picked]).Listbox.Multiple().Placeholder("Choose industries...")
                        .Id("ui-select-multiple")[
                        Industries.Select(name => Ui.SelectOption.Key(name)[name])
                    ]
                ],
                P.Class("text-sm text-ui-muted sm:col-span-2").Data(Testid("ui-select-search-state"))[
                    _searched is null ? "Nothing chosen." : $"Chosen: {_searched}."
                ],
                P.Class("text-sm text-ui-muted").Data(Testid("ui-multiselect-state"))[
                    _industries.Count == 0 ? "Nothing chosen." : $"Chosen: {string.Join(", ", _industries)}."
                ]
            ]);

    private Component ComboboxSection() =>
        Section(
            "Combobox",
            "A text input that filters the list under it as you type. With Filter(false) the page answers each "
            + "keystroke itself — a query, here a list — and Ui.SelectOptionCreate offers to make an option of what was "
            + "typed once it names none that exists.",
            Div.Data(Testid("ui-combobox")).Class("grid max-w-3xl items-start gap-6 sm:grid-cols-3")[
                Ui.Select.Key("combobox").Value(_typed).OnChange(picked => _typed = picked).Combobox.Placeholder("Choose industry...").Id("ui-select-combobox")[
                    Industries.Select(name => Ui.SelectOption.Key(name)[name])
                ],
                Ui.Select.Key("create").Value(_project).OnChange(picked => _project = picked).Combobox.Id("ui-select-create")[
                    Ui.SelectInput.Placeholder("Start typing..."),
                    _projects.Select(project => Ui.SelectOption.Key(project.Id).Value(project.Id)[project.Name]),
                    Ui.SelectOptionCreate.MinLength(2).OnClick(CreateProject)["Create new project"]
                ],
                Ui.Select.Key("backend").Value(_found).OnChange(picked => _found = picked).Combobox.Filter(false)[
                    Ui.SelectInput.Placeholder("Start typing...").OnInput(text => _projectSearch = text),
                    _projects.Where(Named).Select(project => Ui.SelectOption.Key(project.Id).Value(project.Id)[project.Name]),
                    Ui.SelectOptionEmpty.WhenLoading("Loading projects...")["No projects found."]
                ],
                P.Class("text-sm text-ui-muted sm:col-span-3").Data(Testid("ui-combobox-state"))[
                    $"Industry: {_typed ?? "none"}. Project: {ProjectName(_project) ?? "none"}."
                ]
            ]);

    private bool Named((int Id, string Name) project) =>
        project.Name.Contains(_projectSearch, StringComparison.CurrentCultureIgnoreCase);

    private string? ProjectName(int? id) => _projects.Find(project => project.Id == id) is { Id: > 0 } found ? found.Name : null;

    // The page's own answer to the create row: add the option, and pick it.
    private void CreateProject(string name)
    {
        var id = _projects.Max(project => project.Id) + 1;
        _projects.Add((id, name));
        _project = id;
    }

    private static Component Swatch(string hue) =>
        Ui.SelectOption.Key(hue).Value(hue)[
            Div.Class("flex items-center gap-2")[
                Div.Class("size-4 rounded-full").Style($"background:var(--color-{hue}-500)"),
                string.Concat(char.ToUpperInvariant(hue[0]).ToString(), hue.AsSpan(1))
            ]
        ];
}
