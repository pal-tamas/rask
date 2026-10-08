namespace Rask.Site.Features.UiKit;

/// <summary>
///     The kit's form controls: Flux UI's input, textarea, checkbox, radio, switch, calendar, date picker and time
///     picker, example by example, then the controls that are still daisyUI's until each is rebuilt.
/// </summary>
public sealed partial class UiKitDataInputDemo : Component
{
    private string _email = "";
    private string _notes = "";
    private string _code = "";
    private string? _tag;
    private string _search = "Jack Skellington";
    private int _palette;
    private double _volume = 40;
    private int _stars = 4;
    private readonly Signup _signup = new();

    /// <inheritdoc />
    protected override Component? Render() =>
    [
        InputSection(),
        InputGroupSection(),
        TextareaSection(),
        SelectSection(),
        ListboxSection(),
        SearchableSection(),
        ComboboxSection(),
        AutocompleteSection(),
        PillboxSection(),
        PillboxComboboxSection(),
        CheckboxSection(),
        RadioSection(),
        SwitchSection(),
        RangeRatingSection(),
        OneTimeCodeSection(),
        FilterSection(),
        CalendarSection(),
        DatePickerSection(),
        TimePickerSection(),
        FileUploadSection(),
        BoundModelSection(),
        MaskSection()
    ];

    private Component InputSection()
    {
        var badEmail = _email.Length > 0 && !_email.Contains('@');

        return Section(
            "Input — Flux UI's, example by example",
            "Label and Description wrap the input in a field with its error; without them it is the input alone, for "
            + "a Ui.Field of your own. Everything inside the box — an icon, a shortcut, the clear and reveal buttons — "
            + "is a prop. The first field is controlled: its parent decides it is invalid and says why.",
            Div.Data(Testid("ui-text-controls")).Class("grid max-w-3xl gap-6 sm:grid-cols-2")[
                Ui.Field.Key("email")[
                    Ui.Label.Badge("Required")["Email"],
                    Ui.Description["For example, you@example.com."],
                    Ui.Input.Value(_email).Type(InputType.Email).Invalid(badEmail).OnChange(v => { _email = v; }),
                    Ui.Error.Message(badEmail ? "That does not look like an email address." : null)
                ],
                Ui.Input.Of<string>().Key("username").Label("Username").Description("This will be publicly displayed."),
                Ui.Input.Of<string>().Key("mono").Label("Class targeting").Class("max-w-xs").InputClass("font-mono"),
                Ui.Input.Value("password").Key("password").Type(InputType.Password).Label("Password"),
                Ui.Input.Of<string>().Key("date").Type(InputType.Date).Max("2999-12-31").Label("Date"),
                Ui.Input.Of<string>().Key("logo").Type(InputType.File).Label("Logo"),
                Ui.Input.Of<string>().Key("attachments").Type(InputType.File).Label("Attachments").Multiple(),
                Ui.Input.Of<string>().Key("sm").Sm.Placeholder("Filter by..."),
                Ui.Input.Of<string>().Key("disabled").Disabled().Label("Disabled"),
                Ui.Input.Value("BA7K7QZ511S8Z2K").Key("readonly").ReadOnly().Filled.Label("Public API key"),
                Ui.Input.Of<string>().Key("invalid").Invalid().Placeholder("Invalid"),
                Ui.Input.Value("7161234567").Key("mask").Mask("(999) 999-9999").Label("Phone, masked"),
                Ui.Input.Of<string>().Key("icon").Icon(Ui.IconName.MagnifyingGlass).Placeholder("Search orders"),
                Ui.Input.Of<string>().Key("card").IconTrailing(Ui.IconName.CreditCard).Placeholder("4444-4444-4444-4444"),
                Ui.Input.Of<string>().Key("loading").IconTrailing(Ui.IconName.Loading).Placeholder("Search transactions"),
                Ui.Input.Of<string>().Key("slot").Placeholder("Search orders").IconTrailing(
                    Ui.Button.Subtle.Sm.Icon(Ui.IconName.XMark).Class("-mr-1").Aria("label", "Clear")),
                Ui.Input.Value(_search).Key("clearable").Placeholder("Search orders").Clearable()
                    .OnInput(v => _search = v ?? "").OnChange(v => { _search = v; }),
                Ui.Input.Value("password").Key("viewable").Type(InputType.Password).Viewable(),
                Ui.Input.Value("FLUX-1234-5678-ABCD-EFGH").Key("copyable").Icon(Ui.IconName.Key).ReadOnly().Copyable(),
                Ui.Input.Of<string>().Key("kbd").Kbd("⌘K").Icon(Ui.IconName.MagnifyingGlass).Placeholder("Search..."),
                Ui.Input.Of<string>().Key("button").As(Ui.InputAs.Button).Placeholder("Search...")
                    .Icon(Ui.IconName.MagnifyingGlass).Kbd("⌘K").OnClick(() => { _palette++; }),
                P.Class("self-center text-sm text-ui-muted").Data(Testid("ui-input-state"))[
                    $"Searching for \"{_search}\" · palette opened {_palette.ToString(System.Globalization.CultureInfo.InvariantCulture)} times."
                ]
            ]);
    }

    private Component InputGroupSection() =>
        Section(
            "Input group — one outline, shared",
            "A prefix, a suffix or a button fused to the input: the two ends keep their corners and a border is drawn "
            + "once between neighbours. For a label the GROUP goes in a Ui.Field, which reaches the input inside it.",
            Div.Data(Testid("ui-input-group")).Class("grid max-w-3xl gap-6 sm:grid-cols-2")[
                Ui.InputGroup.Key("post")[
                    Ui.Input.Of<string>().Placeholder("Post title"),
                    Ui.Button.Icon(Ui.IconName.Plus)["New post"]
                ],
                Ui.InputGroup.Key("prefix")[
                    Ui.InputGroupPrefix["https://"],
                    Ui.Input.Of<string>().Placeholder("example.com")
                ],
                Ui.InputGroup.Key("suffix")[
                    Ui.Input.Of<string>().Placeholder("chunky-spaceship"),
                    Ui.InputGroupSuffix[".brand.com"]
                ],
                Ui.Field.Key("website")[
                    Ui.Label["Website"],
                    Ui.InputGroup[
                        Ui.InputGroupPrefix["https://"],
                        Ui.Input.Bind(() => _signup.Website).Placeholder("example.com")
                    ],
                    Ui.Error
                ]
            ]);

    private Component TextareaSection() =>
        Section(
            "Textarea — Flux UI's",
            "Four lines unless Rows says otherwise; Rows(UiTextareaRows.Auto) grows with what is typed, which is "
            + "CSS's field-sizing and no script. Resize says which way the reader may drag it.",
            // items-start: a bare textarea is a grid item, and one stretched to its row has nothing to grow into.
            Div.Data(Testid("ui-textarea")).Class("grid max-w-3xl items-start gap-6 sm:grid-cols-2")[
                Ui.Textarea.Value(_notes).Key("notes").Label("Order notes").Badge("Optional")
                    .Placeholder("No lettuce, tomato, or onion...")
                    .OnChange(v => { _notes = v; }),
                Ui.Textarea.Of<string>().Key("two").Rows(2).Label("Note"),
                Ui.Textarea.Of<string>().Key("auto").Rows(UiTextareaRows.Auto)
                    .Placeholder("This textarea will adjust to fit the content..."),
                Div.Key("resize").Class("space-y-4")[
                    Ui.Textarea.Of<string>().Rows(2).Vertical.Placeholder("Resize \"vertical\""),
                    Ui.Textarea.Of<string>().Rows(2).None.Placeholder("Resize \"none\""),
                    Ui.Textarea.Of<string>().Rows(2).Horizontal.Placeholder("Resize \"horizontal\""),
                    Ui.Textarea.Of<string>().Rows(2).Both.Placeholder("Resize \"both\"")
                ],
                P.Class("self-center text-sm text-ui-muted").Data(Testid("ui-textarea-state"))[
                    _notes.Length == 0 ? "No notes yet." : $"Notes: {_notes}"
                ]
            ]);

    private Component RangeRatingSection() =>
        Section(
            "Range and rating",
            "A range can stand on end, and daisyUI puts the low value at the bottom — which is what a "
            + "volume wants and what a rank does not.",
            Div.Data(Testid("ui-range")).Class("grid max-w-sm gap-4")[
                Ui.Range.Value(_volume).Key("vol").Label("Volume").Min(0).Max(100).Step(5)
                    .Accent.OnChange(v => { _volume = v; }),
                Span.Class("text-sm text-ui-muted")[
                    $"Volume: {_volume.ToString("0", System.Globalization.CultureInfo.InvariantCulture)}"
                ],
                Ui.Rating.Value(_stars).Key("stars").Group("score").Label("Rate this").Max(5)
                    .OnChange(v => { _stars = v; })
            ]);

    private Component OneTimeCodeSection() =>
        Section(
            "One-time code",
            "One input drawn as several. Per-digit boxes need script to move focus, defeat the "
            + "browser's SMS autofill, and drop a pasted code entirely into the first box.",
            Div.Data(Testid("ui-otp")).Class("space-y-2")[
                Ui.Otp.Key("otp").Value(_code).Length(6).Label("Verification code").Joined()
                    .Hint("Six digits, sent to your phone.")
                    .Primary.OnChange(v => { _code = v; }),
                P.Class("text-sm text-ui-muted").Data(Testid("ui-otp-state"))[
                    _code.Length == 6 ? "Code complete." : $"{_code.Length} of 6 entered."
                ]
            ]);

    private Component FilterSection() =>
        Section(
            "Filter",
            "Radios rather than buttons: daisyUI hides the unpicked options and shows the reset in "
            + "their place, in CSS, and the group gives a keyboard its arrow keys for free.",
            Div.Data(Testid("ui-filter")).Class("space-y-2")[
                Ui.Filter.Value(_tag).Key("tags").Group("demo-tags")
                    .Options([("bug", "bug"), ("feature", "feature"), ("docs", "docs")])
                    .ResetLabel("All").OnChange(tag => { _tag = tag; }),
                P.Class("text-sm text-ui-muted").Data(Testid("ui-filter-state"))[
                    _tag is null ? "Showing everything." : $"Filtered to {_tag}."
                ]
            ]);

    private Component BoundModelSection() =>
        Section(
            "Bound to a model",
            "The same controls, with no OnChange between them and the model. Bind is the other opening "
            + "of the same chain: it two-way binds, drives the surrounding Form's per-field validation, "
            + "and the control writes back itself.",
            Div.Data(Testid("ui-bound")).Class("space-y-3")[
                Form.Model(_signup)[
                    Div.Class("grid gap-3 sm:grid-cols-2")[
                        Ui.Input.Bind(() => _signup.Email).Label("Email").Type(InputType.Email)
                            .Description("For example, you@example.com."),
                        // T is the model's, so this is a number field with nothing said here.
                        Ui.Input.Bind(() => _signup.Seats).Label("Seats")
                    ],
                    Div.Class("mt-3 flex flex-wrap items-center gap-4")[
                        Ui.Checkbox.Bind(() => _signup.Agreed).Label("I agree to the terms"),
                        Ui.Rating.Bind(() => _signup.Score).Group("bound-score").Label("Rate this").Max(5)
                    ]
                ],
                P.Class("text-sm text-ui-muted").Data(Testid("ui-bound-state"))[
                    $"{_signup.Email} · {_signup.Seats.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
                    + $" seats · agreed {(_signup.Agreed ? "yes" : "no")}"
                    + $" · {_signup.Score.ToString(System.Globalization.CultureInfo.InvariantCulture)} stars"
                ]
            ]);

    private static Component MaskSection() =>
        Section(
            "Mask",
            "A closed set of shapes. Whatever is masked has to survive losing its corners.",
            Div.Data(Testid("ui-mask")).Class("flex flex-wrap gap-3")[
                Masked("circle", Ui.MaskShape.Circle),
                Masked("squircle", Ui.MaskShape.Squircle),
                Masked("hexagon", Ui.MaskShape.Hexagon),
                Masked("star", Ui.MaskShape.Star2),
                Masked("heart", Ui.MaskShape.Heart)
            ]);

    private static AttrBag Testid(string value) => new("testid", value);

    private static UiMask Masked(string key, Ui.MaskShape shape) =>
        Ui.Mask.Key(key).Shape(shape).Class("size-14 bg-primary");

    private static Component Section(string heading, string blurb, Component body) =>
        Div.Key(heading).Class("mb-8")[
            H2.Class("text-lg font-semibold tracking-tight")[heading],
            P.Class("mt-1 mb-3 text-sm text-ui-muted")[blurb],
            body
        ];

    // An ordinary model, which is the point: nothing on it knows it is being edited by a UI kit.
    public sealed class Signup
    {
        public string Email { get; set; } = "";

        public int Seats { get; set; } = 1;

        public string Website { get; set; } = "";

        public bool Agreed { get; set; }

        public int Score { get; set; }
    }
}
