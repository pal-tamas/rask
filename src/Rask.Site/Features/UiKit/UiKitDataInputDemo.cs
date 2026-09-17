namespace Rask.Site.Features.UiKit;

/// <summary>
///     daisyUI's Data input category, drawn with the kit.
/// </summary>
/// <remarks>
///     Every control here takes a required label, and it becomes the accessible name rather than a
///     placeholder. A placeholder disappears the moment typing starts, so the one thing saying what a
///     field is for vanishes exactly when a reader might check it.
/// </remarks>
public sealed partial class UiKitDataInputDemo : Component
{
    private string _email = "";
    private string _notes = "";
    private string _code = "";
    private string? _tag;
    private bool _remember = true;
    private bool _alerts;
    private string _shipping = "standard";
    private string? _country;
    private string? _framework;
    private string? _home;
    private string _search = "";
    private readonly List<string> _packages = ["core", "ui"];
    private string _plan = "pro";
    private string _density = "cosy";
    private List<string> _topics = ["releases"];
    private double _volume = 40;
    private int _stars = 4;
    private DateOnly _month = DateOnly.FromDateTime(DateTime.Today);
    private DateOnly? _date;
    private readonly List<string> _dropped = [];
    private UiDateRange _stay;
    private DateOnly _arrival;
    private List<DateOnly> _daysOff = [];
    private readonly Signup _signup = new();

    // Words with an accent in them, on purpose: the default match ignores case AND accents in the
    // visitor's own culture, so "oster" finds Österreich.
    private static readonly (string Value, string Text)[] Countries =
    [
        ("at", "Österreich"), ("ch", "Schweiz"), ("cz", "Česko"), ("de", "Deutschland"),
        ("es", "España"), ("hu", "Magyarország"), ("ie", "Ireland"), ("gb", "United Kingdom")
    ];

    /// <inheritdoc />
    protected override Component? Render() =>
    [
        Section(
            "Text controls",
            "Tone colours the border — which is how a field says it is in error without a second "
            + "element — and Ghost is the borderless form. daisyUI defines no outline, soft or dash for "
            + "a text control, so those draw the default rather than a class that does nothing.",
            Div.Data(Testid("ui-text-controls")).Class("grid gap-3 sm:grid-cols-2")[
                // Value opens the chain on every control in the kit now: they are all
                // IFormControl<T>, so the opening step fixes the value type and the mode at once, and
                // Label, Type and the rest follow it. Of<T>() is the opening for a field with no value
                // to start from.
                // The message is the FIELD's, not a sibling placed after it. It used to be a detached
                // UiValidator at the end of this grid, which worked only because the input was a bare
                // element: daisyUI reveals the hint with `.validator ~ .validator-hint`, so a field that
                // grew a label and a wrapper stopped being its sibling and the message silently vanished.
                // The badge sits inside the label and is hidden from assistive tech; the hint and, once it
                // shows, the error are tied to the input by aria-describedby, error first.
                UiInput.Value(_email).Key("email").Label("Email").Badge("Required").Type(InputType.Email)
                    .Hint("For example, you@example.com.")
                    .Tone(_email.Length > 0 && !_email.Contains('@') ? UiTone.Error : (UiTone?)null)
                    .Error(_email.Length > 0 && !_email.Contains('@')
                        ? "That does not look like an email address."
                        : null)
                    .OnChange(v => { _email = v; }),
                // Anything inside the box — an icon, a shortcut, a clear button — makes the box a container
                // around the input, and the label keeps its place above the field: a floating caption rises
                // through exactly the room the icon now occupies.
                UiInput.Value(_search).Key("search").Label("Search")
                    .Icon(UiIconName.Search).Kbd("⌘K").Clearable(true)
                    .Placeholder("Find a package")
                    .OnInput(v => _search = v ?? ""),
                // AutoSize is CSS — `field-sizing: content` — so the box grows as you type with no runtime at
                // all, and where an engine has not shipped it the box keeps its Rows and scrolls.
                UiTextarea.Value(_notes).Key("notes").Label("Notes").Badge("Optional").Rows(2)
                    .AutoSize(true)
                    .Resize(UiResize.None)
                    .Hint("Anything else? The box grows as you type.")
                    .OnChange(v => { _notes = v; }),
                UiSelect.Value(_country).Key("country")
                    .Options([("hu", "Hungary"), ("gb", "United Kingdom")])
                    .Label("Country")
                    .Placeholder("Choose…")
                    .OnChange(v => { _country = v; }),
                UiFileInput.Value("").Key("avatar").Label("Avatar").Size(UiSize.Sm)
            ]),

        Section(
            "Select — the platform's, and the drawn one",
            "Native is the default and the one to reach for. Turn it off when the list has to carry "
            + "more than the platform will show — groups, unavailable options — or has to escape an "
            + "overflow:hidden ancestor, which the box below is. The drawn list needs the runtime.",
            Div.Data(Testid("ui-select")).Class("grid gap-3 sm:grid-cols-2")[
                Div.Class("h-24 overflow-hidden rounded-xl border border-base-300 p-3")[
                    UiSelect.Value(_framework).Key("fw")
                        .Options([
                            ("core", "Rask.Core"), ("ui", "Rask.Ui"), ("cli", "Rask.Cli"),
                            ("blazor", "Rask.Blazor"), ("ext", "Rask.External")
                        ])
                        .Label("Framework")
                        .Placeholder("Choose a package")
                        .Native(false)
                        .OptionGroup(v => v is "core" or "ui" ? "Rendering" : "Tooling")
                        .OptionDisabled(v => v == "blazor")
                        .OnChange(v => { _framework = v; })
                ],
                P.Class("self-center text-sm text-ui-muted").Data(Testid("ui-select-state"))[
                    _framework is null ? "Nothing chosen." : $"Chosen: {_framework}."
                ]
            ]),

        Section(
            "Searchable — the same control, typed into",
            "There is no separate combobox: a box you type into to narrow a fixed set of answers is the "
            + "same question a select asks. Searchable adds the search box, matching case- and "
            + "accent-insensitively in your own culture — type \"oster\" to find Österreich. Filter says "
            + "what a match is when the words shown are not the whole answer; this one searches the "
            + "country CODE as well. Clearable puts the field back to nothing chosen.",
            Div.Data(Testid("ui-select-search")).Class("grid gap-3 sm:grid-cols-2")[
                UiSelect.Value(_home).Key("home")
                    .Options(Countries)
                    .Label("Country")
                    .Placeholder("Search countries")
                    .Searchable(true)
                    .Clearable(true)
                    .Filter((v, text) =>
                        v.Contains(text, StringComparison.OrdinalIgnoreCase)
                        || Countries.Any(o => o.Value == v
                                              && o.Text.Contains(text, StringComparison.CurrentCultureIgnoreCase)))
                    .OnChange(v => { _home = v; }),
                P.Class("self-center text-sm text-ui-muted").Data(Testid("ui-select-search-state"))[
                    _home is null ? "Nothing chosen." : $"Chosen: {_home}."
                ]
            ]),

        Section(
            "Multi-select — several answers, one field",
            "The same name, for a field that holds a collection: bind a List, array or HashSet and "
            + "UiSelect is a multi-select — the model says so, not a flag. Native is a real "
            + "multi-select: no script, and it posts on its own. The drawn one shows the answers as "
            + "chips you can remove one at a time, keeps the list open while you pick, and adds the "
            + "search box and select-all a long list needs.",
            Div.Data(Testid("ui-multiselect")).Class("grid gap-3 sm:grid-cols-2")[
                UiSelect.Values(_packages).Key("pkgs")
                    .Options([
                        ("core", "Rask.Core"), ("ui", "Rask.Ui"), ("cli", "Rask.Cli"),
                        ("blazor", "Rask.Blazor"), ("ext", "Rask.External")
                    ])
                    .Label("Packages")
                    .Placeholder("Choose packages")
                    .Native(false)
                    .SelectAll(true)
                    .Filter((v, text) => v.Contains(text, StringComparison.OrdinalIgnoreCase))
                    .OptionGroup(v => v is "core" or "ui" ? "Rendering" : "Tooling")
                    .OptionDisabled(v => v == "blazor")
                    .OnChange(Choose),
                P.Class("self-center text-sm text-ui-muted").Data(Testid("ui-multiselect-state"))[
                    _packages.Count == 0
                        ? "Nothing chosen."
                        : $"Chosen: {string.Join(", ", _packages)}."
                ]
            ]),

        Section(
            "Choice lists — a whole set as one field",
            "A radio group binds the GROUP's value and a checkbox group binds the collection your model "
            + "declares, so each is one field rather than one per option. Layout is Flux's set of looks: a "
            + "list, cards with room for a description, pills, buttons, or one segmented strip. Every one of "
            + "them keeps a real input inside its label — a card and a pill look like buttons, and a button "
            + "is the one thing a choice must not be, because the grouping, the arrow keys, the space bar "
            + "and the form post all come from the input being there.",
            Div.Data(Testid("ui-choice-lists")).Class("grid gap-5 lg:grid-cols-2")[
                UiRadioGroup.Value(_plan).Key("plan")
                    .Options([("free", "Free"), ("pro", "Pro"), ("team", "Team")])
                    .Label("Plan")
                    .Layout(UiChoiceLayout.Cards)
                    .OptionDescription(v => v switch
                    {
                        "pro" => "Everything, billed monthly.",
                        "team" => "Seats, roles and shared billing.",
                        _ => "For trying it out."
                    })
                    .OnChange(v => { _plan = v; }),
                Div.Class("flex flex-col gap-5")[
                    UiRadioGroup.Value(_density).Key("density")
                        .Options([("cosy", "Cosy"), ("compact", "Compact")])
                        .Label("Density")
                        .Layout(UiChoiceLayout.Segmented)
                        .OnChange(v => { _density = v; }),
                    UiCheckboxGroup.Values(_topics).Key("topics")
                        .Options([("news", "News"), ("releases", "Releases"), ("jobs", "Jobs")])
                        .Label("Email me about")
                        .Layout(UiChoiceLayout.Pills)
                        .CheckAll(true)
                        .OnChange(v => { _topics = [.. v]; }),
                    P.Class("text-sm text-ui-muted").Data(Testid("ui-choice-state"))[
                        _topics.Count == 0
                            ? $"{_plan}, {_density}, nothing subscribed."
                            : $"{_plan}, {_density}, {string.Join(", ", _topics)}."
                    ]
                ]
            ]),

        Section(
            "Labels",
            "A labelled text field floats its label by default: the caption sits in the field until there "
            + "is content, then rises out of the way, and it is the field's real label. Floating(false) puts "
            + "it above the field instead. A caption inside the frame, for a unit or a currency, is only "
            + "decoration, so that field is named separately.",
            Div.Data(Testid("ui-labels")).Class("grid gap-3 sm:grid-cols-2")[
                UiInput.Of<string>().Key("float").Label("Company"),
                UiInput.Of<string>().Key("legend").Label("Company number").Floating(false),
                UiLabel.Key("price").Text("€").Trailing("per month")[
                    UiInput.Of<string>().AccessibleLabel("Price per month").Placeholder("29")
                ]
            ]),

        Section(
            "Choices",
            "The words are part of the hit target: on a phone a 16px box on its own is the difference "
            + "between a control and a dare.",
            Div.Data(Testid("ui-choices")).Class("flex flex-wrap items-center gap-4")[
                UiCheckbox.Value(_remember).Key("remember").Text("Remember me").Tone(UiTone.Primary)
                    .OnChange(v => { _remember = v; }),
                UiToggle.Value(_alerts).Key("alerts").Text("Email alerts").Tone(UiTone.Success)
                    .OnChange(v => { _alerts = v; }),
                // A radio binds its OWN checked state, so it only ever reports true — choosing one
                // fires nothing on the option it deselected. The group's value belongs to the group.
                UiRadio.Value(_shipping == "standard").Key("std").Text("Standard").Group("shipping")
                    .OnChange(_ => { _shipping = "standard"; }),
                UiRadio.Value(_shipping == "express").Key("exp").Text("Express").Group("shipping")
                    .OnChange(_ => { _shipping = "express"; })
            ]),

        Section(
            "Range and rating",
            "A range can stand on end, and daisyUI puts the low value at the bottom — which is what a "
            + "volume wants and what a rank does not.",
            Div.Data(Testid("ui-range")).Class("grid max-w-sm gap-4")[
                UiRange.Value(_volume).Key("vol").Label("Volume").Min(0).Max(100).Step(5)
                    .Tone(UiTone.Accent).OnChange(v => { _volume = v; }),
                Span.Class("text-sm text-ui-muted")[
                    $"Volume: {_volume.ToString("0", System.Globalization.CultureInfo.InvariantCulture)}"
                ],
                UiRating.Value(_stars).Key("stars").Group("score").Label("Rate this").Max(5)
                    .OnChange(v => { _stars = v; })
            ]),

        Section(
            "One-time code",
            "One input drawn as several. Per-digit boxes need script to move focus, defeat the "
            + "browser's SMS autofill, and drop a pasted code entirely into the first box.",
            Div.Data(Testid("ui-otp")).Class("space-y-2")[
                UiOtp.Value(_code).Key("otp").Label("Verification code").Length(6).Joined(true)
                    .Tone(UiTone.Primary).OnChange(v => { _code = v; }),
                P.Class("text-sm text-ui-muted").Data(Testid("ui-otp-state"))[
                    _code.Length == 6 ? "Code complete." : $"{_code.Length} of 6 entered."
                ]
            ]),

        Section(
            "Filter",
            "Radios rather than buttons: daisyUI hides the unpicked options and shows the reset in "
            + "their place, in CSS, and the group gives a keyboard its arrow keys for free.",
            Div.Data(Testid("ui-filter")).Class("space-y-2")[
                UiFilter.Value(_tag).Key("tags").Group("demo-tags")
                    .Options([("bug", "bug"), ("feature", "feature"), ("docs", "docs")])
                    .ResetLabel("All").OnChange(tag => { _tag = tag; }),
                P.Class("text-sm text-ui-muted").Data(Testid("ui-filter-state"))[
                    _tag is null ? "Showing everything." : $"Filtered to {_tag}."
                ]
            ]),

        Section(
            "Calendar",
            "Built in C#, because the element daisyUI styles for this is a JavaScript web component the "
            + "kit does not ship. Every day is a button carrying its full date as its name.",
            Div.Data(Testid("ui-calendar")).Class("space-y-2")[
                // Month and OnMonth are the VIEW, not the value: paging through months changes nothing
                // a form would submit, which is why they are not part of the binding.
                UiCalendar
                    .Value(_date ?? default)
                    .Label("Delivery date")
                    .Month(_month)
                    .OnMonth(m => { _month = m; })
                    .OnChange(d => { _date = d; })
                    .Class("max-w-xs"),
                P.Class("text-sm text-ui-muted").Data(Testid("ui-calendar-state"))[
                    _date is { } picked
                        ? $"Chosen: {picked.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)}"
                        : "No date chosen."
                ]
            ]),

        Section(
            "Several days, a range, and a picker",
            "The same entries. Bind a collection of days and UiCalendar picks several; bind a UiDateRange and it "
            + "picks a range — the first click is held and drawn, the second writes the whole range, so the model "
            + "never holds half of one. UiDatePicker is the field-shaped button that opens the grid in a popover: "
            + "a single day closes it on the pick, several days keep it open, a range closes on its second click.",
            Div.Data(Testid("ui-dates")).Class("grid gap-4 md:grid-cols-2")[
                Div.Class("space-y-2")[
                    UiCalendar.Value(_stay).Label("Stay").Class("max-w-xs")
                        .OnChange(r => { _stay = r; }),
                    P.Class("text-sm text-ui-muted").Data(Testid("ui-dates-stay"))[
                        _stay == default
                            ? "No stay chosen."
                            : $"Stay: {Iso(_stay.Start)} to {Iso(_stay.End)}"
                    ]
                ],
                Div.Class("space-y-3")[
                    UiDatePicker.Value(_arrival).Label("Arrival")
                        .OnChange(d => { _arrival = d; }),
                    UiDatePicker.Values(_daysOff).Label("Days off")
                        .OnChange(days => { _daysOff = [.. days]; }),
                    UiDatePicker.Value(_stay).Label("Stay, as a field")
                        .OnChange(r => { _stay = r; }),
                    P.Class("text-sm text-ui-muted").Data(Testid("ui-dates-picked"))[
                        _arrival == default ? "No arrival chosen." : $"Arrival: {Iso(_arrival)}",
                        $" · {_daysOff.Count} days off"
                    ]
                ]
            ]),

        Section(
            "File drop area",
            "Still the native file input, stretched invisibly over the whole area: a click anywhere opens the "
            + "picker and a file dropped anywhere lands in the input, which the browser already does with no "
            + "script. The runtime only marks the area while a file is dragged over it.",
            Div.Data(Testid("ui-dropzone")).Class("max-w-md space-y-2")[
                UiFileInput.Value("").Key("receipts").Label("Receipts").Id("demo-receipts")
                    .Dropzone(true)
                    .Heading("Drop receipts here, or click to choose")
                    .Text("PDF or JPG, several at once")
                    .Accept(".pdf,.jpg,.jpeg")
                    .Multiple(true)
                    .OnFiles(files =>
                    {
                        _dropped.Clear();
                        _dropped.AddRange(files.Select(f => f.Name));
                    }),
                P.Class("text-sm text-ui-muted").Data(Testid("ui-dropzone-state"))[
                    _dropped.Count == 0 ? "No files yet." : "Chosen: " + string.Join(", ", _dropped)
                ]
            ]),

        Section(
            "Bound to a model",
            "The same controls, with no OnChange between them and the model. Bind is the other opening "
            + "of the same chain: it two-way binds, drives the surrounding Form's per-field validation, "
            + "and the control writes back itself.",
            Div.Data(Testid("ui-bound")).Class("space-y-3")[
                Form.Model(_signup)[
                    Div.Class("grid gap-3 sm:grid-cols-2")[
                        UiInput.Bind(() => _signup.Email).Label("Email").Type(InputType.Email)
                            .Hint("For example, you@example.com."),
                        // T is the model's, so this is a number field with nothing said here.
                        UiInput.Bind(() => _signup.Seats).Label("Seats")
                    ],
                    Div.Class("mt-3 flex flex-wrap items-center gap-4")[
                        UiCheckbox.Bind(() => _signup.Agreed).Text("I agree to the terms"),
                        UiRating.Bind(() => _signup.Score).Group("bound-score").Label("Rate this").Max(5)
                    ]
                ],
                P.Class("text-sm text-ui-muted").Data(Testid("ui-bound-state"))[
                    $"{_signup.Email} · {_signup.Seats.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
                    + $" seats · agreed {(_signup.Agreed ? "yes" : "no")}"
                    + $" · {_signup.Score.ToString(System.Globalization.CultureInfo.InvariantCulture)} stars"
                ]
            ]),

        Section(
            "Mask",
            "A closed set of shapes. Whatever is masked has to survive losing its corners.",
            Div.Data(Testid("ui-mask")).Class("flex flex-wrap gap-3")[
                Masked("circle", UiMaskShape.Circle),
                Masked("squircle", UiMaskShape.Squircle),
                Masked("hexagon", UiMaskShape.Hexagon),
                Masked("star", UiMaskShape.Star2),
                Masked("heart", UiMaskShape.Heart)
            ])
    ];

    private static Dictionary<string, string?> Testid(string value) => new() { ["testid"] = value };

    private static string Iso(DateOnly date) =>
        date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static Component Masked(string key, UiMaskShape shape) =>
        UiMask.Key(key).Shape(shape).Class("size-14 bg-primary");

    // Controlled mode hands over a fresh collection every time — see UiMultiSelect's OnChange. The
    // demo holds one list and refills it, which is what a model with a get-only collection does too.
    private void Choose(ICollection<string> picked)
    {
        _packages.Clear();
        _packages.AddRange(picked);
    }

    private static Component Section(string heading, string blurb, Component body) =>
        Div.Key(heading).Class("mb-8")[
            H2.Class("text-lg font-semibold tracking-tight")[heading],
            P.Class("mt-1 mb-3 text-sm text-ui-muted")[blurb],
            body
        ];

    // An ordinary model, which is the point: nothing on it knows it is being edited by a UI kit.
    private sealed class Signup
    {
        public string Email { get; set; } = "";

        public int Seats { get; set; } = 1;

        public bool Agreed { get; set; }

        public int Score { get; set; }
    }
}
