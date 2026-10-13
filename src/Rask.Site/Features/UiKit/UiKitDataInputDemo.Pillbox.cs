namespace Rask.Site.Features.UiKit;

// Flux UI's pillbox page, example by example: https://fluxui.dev/components/pillbox
public sealed partial class UiKitDataInputDemo
{
    private static readonly string[] TagNames =
        ["Design", "Development", "Marketing", "Sales", "Support", "Engineering", "Product", "Operations"];

    private static readonly string[] SkillNames =
        ["JavaScript", "TypeScript", "PHP", "Python", "Ruby", "Go", "Rust", "Java", "C#", "Swift"];

    private static readonly (string Name, Ui.IconName Icon)[] Platforms =
        [("GitHub", Ui.IconName.CodeBracket), ("GitLab", Ui.IconName.Server), ("Bitbucket", Ui.IconName.Cloud)];

    private readonly List<(int Id, string Name)> _tagList = [.. TagNames.Select((name, index) => (index + 1, name))];

    private List<string> _pillTags = [];
    private List<string> _pillSmall = [];
    private List<string> _pillSkills = [];
    private List<string> _pillPlatforms = [];
    private List<string> _pillTyped = [];
    private List<int> _pillCreated = [];
    private List<int> _pillFound = [];
    private List<int> _pillNamed = [];
    private string _tagSearch = "";
    private string _tagQuery = "";
    private int _tagQueries;
    private List<string> _pillStored = [];
    private string[] _skillsFound = SkillNames;
    private int _skillQueries;
    private bool _namingTag;
    private string _tagName = "";

    private Component PillboxSection() =>
        Section(
            "Pillbox",
            "Several answers out of a list, each shown as a pill that can be taken off again. Sm is the smaller one, "
            + "Searchable puts a search field over the options, and an option's content can be more than words. "
            + "Ui.PillboxSearch with Filter(false) and Debounce asks the page for the options once typing has paused.",
            Div.Data(Testid("ui-pillbox")).Class("grid max-w-3xl items-start gap-6 sm:grid-cols-2")[
                Div.Class("max-w-80")[
                    Ui.Pillbox.Key("tags").Values(_pillTags).OnChange(picked => _pillTags = [.. picked]).Placeholder("Choose tags...").Id("ui-pillbox-tags")[
                        TagNames.Select(name => Ui.PillboxOption.Key("tags-" + name)[name])
                    ]
                ],
                Div.Class("max-w-80")[
                    Ui.Pillbox.Key("small").Values(_pillSmall).OnChange(picked => _pillSmall = [.. picked]).Sm.Placeholder("Choose tags...")[
                        TagNames.Select(name => Ui.PillboxOption.Key("small-" + name)[name])
                    ]
                ],
                Div.Class("max-w-80")[
                    Ui.Pillbox.Key("searchable").Values(_pillSkills).OnChange(picked => _pillSkills = [.. picked]).Searchable()
                        .SearchPlaceholder("Filter skills...").Placeholder("Choose skills...").Id("ui-pillbox-searchable")[
                        SkillNames.Select(name => Ui.PillboxOption.Key("searchable-" + name)[name])
                    ]
                ],
                Div.Class("max-w-80")[
                    Ui.Pillbox.Key("icons").Values(_pillPlatforms).OnChange(picked => _pillPlatforms = [.. picked]).Placeholder("Choose platforms...")[
                        Platforms.Select(platform => Ui.PillboxOption.Key("icons-" + platform.Name).Value(platform.Name)[
                            Div.Class("flex items-center gap-2")[Ui.Icon.Name(platform.Icon).Mini.Class("text-zinc-400"), platform.Name]
                        ])
                    ]
                ],
                Div.Class("max-w-80")[
                    Ui.Pillbox.Key("store").Values(_pillStored).OnChange(picked => _pillStored = [.. picked]).Filter(false)
                        .Placeholder("Choose skills...").Id("ui-pillbox-store")[
                        Ui.PillboxSearch.Placeholder("Search skills...").OnInput(SearchSkills).Debounce(250.Milliseconds),
                        _skillsFound.Select(name => Ui.PillboxOption.Key("store-" + name)[name])
                    ]
                ],
                P.Class("text-sm text-ui-muted sm:col-span-2").Data(Testid("ui-pillbox-state"))[
                    $"Tags: {Listed(_pillTags)}. Skills: {Listed(_pillSkills)}. Stored: {Listed(_pillStored)}. Searches: {_skillQueries}."
                ]
            ]);

    private Component PillboxComboboxSection() =>
        Section(
            "Pillbox combobox",
            "Combobox puts an input among the pills: typing narrows the list, and Backspace in the empty input takes "
            + "the last pill off. Ui.PillboxOptionCreate offers to make an option of what was typed; with Filter(false) "
            + "the page answers the search itself, once typing has paused for Debounce on Ui.PillboxInput, and the "
            + "pills stay whatever it answers; and with nothing to type into, the create row opens a form of the page's own.",
            Div.Data(Testid("ui-pillbox-combobox")).Class("grid max-w-3xl items-start gap-6 sm:grid-cols-2")[
                Div.Class("max-w-80")[
                    Ui.Pillbox.Key("combobox").Values(_pillTyped).OnChange(picked => _pillTyped = [.. picked]).Combobox.Placeholder("Choose skills...")
                        .Id("ui-pillbox-combobox")[
                        SkillNames.Select(name => Ui.PillboxOption.Key("combobox-" + name)[name])
                    ]
                ],
                Div.Class("max-w-80")[
                    Ui.Pillbox.Key("create").Values(_pillCreated).OnChange(picked => _pillCreated = [.. picked]).Combobox.Id("ui-pillbox-create")[
                        Ui.PillboxInput.Value(_tagSearch).OnInput(text => _tagSearch = text).Placeholder("Choose tags..."),
                        _tagList.Select(tag => Ui.PillboxOption.Key("create-" + tag.Id).Value(tag.Id)[tag.Name]),
                        Ui.PillboxOptionCreate.MinLength(2).OnClick(CreateTag)[$"Create new \"{_tagSearch}\""]
                    ]
                ],
                Div.Class("max-w-80")[
                    Ui.Pillbox.Key("backend").Values(_pillFound).OnChange(picked => _pillFound = [.. picked]).Combobox.Filter(false).Id("ui-pillbox-backend")[
                        Ui.PillboxInput.OnInput(QueryTags).Debounce(250.Milliseconds).Placeholder("Choose tags..."),
                        _tagList.Where(Queried).Select(tag => Ui.PillboxOption.Key("backend-" + tag.Id).Value(tag.Id)[tag.Name]),
                        Ui.PillboxOptionEmpty.WhenLoading("Loading tags...")["No tags found."]
                    ]
                ],
                Div.Class("grid gap-3")[
                    Ui.Pillbox.Key("form").Values(_pillNamed).OnChange(picked => _pillNamed = [.. picked]).Placeholder("Choose tags...").Id("ui-pillbox-form")[
                        Ui.PillboxOptionCreate.OnClick(_ => _namingTag = true)["Create new"],
                        _tagList.Select(tag => Ui.PillboxOption.Key("form-" + tag.Id).Value(tag.Id)[tag.Name])
                    ],
                    _namingTag
                        ? Div.Class("flex items-end gap-3").Data(Testid("ui-pillbox-new-tag"))[
                            Ui.Input.Value(_tagName).OnChange(name => _tagName = name).Label("Name").Placeholder("e.g. 'Research'"),
                            Ui.Button.Primary.OnClick(NameTag)["Create"]
                        ]
                        : null
                ],
                P.Class("text-sm text-ui-muted sm:col-span-2").Data(Testid("ui-pillbox-combobox-state"))[
                    $"Skills: {Listed(_pillTyped)}. Tags: {Listed(_pillCreated.Select(TagName))}. "
                    + $"Found: {Listed(_pillFound.Select(TagName))}. Searches: {_tagQueries}."
                ]
            ]);

    // The page's answer to the search: a query, here over a list.
    private void SearchSkills(string text)
    {
        _skillQueries++;
        _skillsFound = [.. SkillNames.Where(name => name.Contains(text, StringComparison.CurrentCultureIgnoreCase))];
    }

    private void QueryTags(string text)
    {
        _tagQueries++;
        _tagQuery = text;
    }

    private static string Listed(IEnumerable<string> names) => string.Join(", ", names) is { Length: > 0 } listed ? listed : "none";

    private string TagName(int id) => _tagList.Find(tag => tag.Id == id).Name;

    private bool Queried((int Id, string Name) tag) => tag.Name.Contains(_tagQuery, StringComparison.CurrentCultureIgnoreCase);

    // The page's own answer to the create row: add the option, pick it, and forget what was typed.
    private void CreateTag(string name)
    {
        var id = AddTag(name);
        _pillCreated = [.. _pillCreated, id];
        _tagSearch = "";
    }

    private void NameTag()
    {
        if (_tagName.Length > 0)
        {
            _pillNamed = [.. _pillNamed, AddTag(_tagName)];
        }

        _tagName = "";
        _namingTag = false;
    }

    private int AddTag(string name)
    {
        var id = _tagList.Max(tag => tag.Id) + 1;
        _tagList.Add((id, name));

        return id;
    }
}
