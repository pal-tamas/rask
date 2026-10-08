namespace Rask.Site.Features.UiKit;

/// <summary>
///     The kit's form controls: Flux UI's input, textarea, checkbox, radio and switch, example by example, then
///     the controls that are still daisyUI's until each is rebuilt.
/// </summary>
public sealed partial class UiKitDataInputDemo : Component
{
    private string _email = "";
    private string _notes = "";
    // One-time codes, one per example: the plain row, the form, the auto-submitting one, the licence key, the
    // PIN, and the three laid out by hand.
    private readonly string[] _codes = ["", "", "", "L49R4", "1234", "", "", ""];
    private string? _verified;
    private string? _tag;
    private string _search = "Jack Skellington";
    private int _palette;
    // Sliders, one value per example.
    private readonly int[] _slides = [500, 50, 50, 25, 3, 4, 3, 3, 500, 500];
    private int[] _band = [200, 800];
    private int[] _apart = [450, 550];
    private int[] _price = [200, 800];
    private int _stars = 4;
    private DateOnly _month = DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().Date);
    private DateOnly? _date;
    private readonly List<string> _dropped = [];
    private UiDateRange _stay;
    private DateOnly _arrival;
    private List<DateOnly> _daysOff = [];
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
        SliderSection(),
        SliderTicksSection(),
        RangeSliderSection(),
        RatingSection(),
        OneTimeCodeSection(),
        OneTimeCodeLayoutSection(),
        FilterSection(),
        CalendarSection(),
        SeveralDaysRangeSection(),
        FileDropAreaSection(),
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

    private Component SliderSection() =>
        Section(
            "Slider — Flux UI's, example by example",
            "A real range input under each thumb, so dragging, a press on the track, the arrow keys, Page Up / Page "
            + "Down and Home / End are the browser's own. The value follows the thumb while it moves, and BigStep is "
            + "how far Shift with an arrow key takes it.",
            Div.Data(Testid("ui-slider")).Class("grid max-w-3xl gap-6 sm:grid-cols-2")[
                Slide(0).Key("basic").Max(1000),
                Slide(1).Key("step").Min(0).Max(100).Step(10),
                Ui.Field.Key("value")[
                    Ui.Label.Trailing(Span.Class("tabular-nums").Data(Testid("ui-slider-value"))[Number(_slides[2])])["Corner radius"],
                    Slide(2)
                ],
                Ui.Field.Key("input")[
                    Ui.Label["Corner radius"],
                    Div.Class("-mt-2 flex items-center gap-4")[
                        Slide(3),
                        Ui.Input.Value(_slides[3]).Type(InputType.Number).Sm.Class("max-w-18").OnChange(v => { _slides[3] = v; })
                    ]
                ],
                Ui.Field.Key("big")[
                    Ui.Label.Trailing(Span.Class("tabular-nums").Data(Testid("ui-slider-big"))[Number(_slides[9])])["Big steps"],
                    Slide(9).Min(0).Max(1000).Step(1).BigStep(100),
                    Ui.Description["Hold Shift and press an arrow key to move by 100."]
                ],
                Slide(8).Key("styles").Max(1000).TrackClass("h-5").ThumbClass("size-5"),
                Ui.Field.Key("disabled")[
                    Ui.Label["Disabled"],
                    Ui.Slider.Value(30).Disabled()
                ]
            ]);

    private Component SliderTicksSection() =>
        Section(
            "Slider ticks",
            "Ui.SliderTick marks a value: a line, a dot on the track, or a label. Pressing one moves the thumb there.",
            Div.Data(Testid("ui-slider-ticks")).Class("grid max-w-3xl gap-6 sm:grid-cols-2")[
                Slide(4).Key("marks").Min(1).Max(5)[Steps(step => Ui.SliderTick.Value(step))],
                Slide(5).Key("dots").Min(1).Max(5).Inside.TrackClass("h-5").ThumbClass("size-6")[
                    Steps(step => Ui.SliderTick.Value(step).Dot)
                ],
                Slide(6).Key("numbers").Min(1).Max(5)[Steps(step => Ui.SliderTick.Value(step)[Number(step)])],
                Slide(7).Key("custom").Min(1).Max(5)[
                    Ui.SliderTick.Value(1)["Low"],
                    Ui.SliderTick.Value(3)["Mid"],
                    Ui.SliderTick.Value(5)["High"]
                ]
            ]);

    private Component RangeSliderSection() =>
        Section(
            "Range slider",
            "Two thumbs over an array of two. They do not cross, MinStepsBetween keeps them further apart, and a press "
            + "on the track moves the nearer one.",
            Div.Data(Testid("ui-slider-range")).Class("grid max-w-3xl gap-6 sm:grid-cols-2")[
                Ui.Slider.Value(_band).Key("range").Range().Max(1000).OnChange(v => { _band = v; }),
                Ui.Slider.Value(_apart).Key("apart").Range().Max(1000).Step(1).MinStepsBetween(100).OnChange(v => { _apart = v; }),
                Ui.Field.Key("price")[
                    Ui.Label.Trailing(Span.Class("tabular-nums").Data(Testid("ui-slider-price"))[
                        $"${Number(_price[0])} – ${Number(_price[1])}"
                    ])["Price range"],
                    Ui.Slider.Value(_price).Range().Min(0).Max(990).Step(10).MinStepsBetween(10).BigStep(100)
                        .OnChange(v => { _price = v; })
                ]
            ]);

    private Component RatingSection() =>
        Section(
            "Rating",
            "Radios sharing a name, so the arrow keys move between stars and a screen reader hears a group.",
            Div.Data(Testid("ui-rating")).Class("grid max-w-sm gap-4")[
                Ui.Rating.Value(_stars).Key("stars").Group("score").Label("Rate this").Max(5)
                    .OnChange(v => { _stars = v; })
            ]);

    private Component OneTimeCodeSection() =>
        Section(
            "OTP input — Flux UI's, example by example",
            "One real text input per character, and the code they spell kept in one string. A character moves on to "
            + "the next cell, Backspace walks back, and a pasted code — or one the phone offers — fills every cell "
            + "from the first. OnComplete runs when the last cell is filled.",
            Div.Data(Testid("ui-otp")).Class("grid max-w-3xl gap-6 sm:grid-cols-2")[
                Div.Key("basic").Class("space-y-2")[
                    Code(0).Length(6),
                    P.Class("text-sm text-ui-muted").Data(Testid("ui-otp-state"))[
                        _codes[0].Length == 6 ? $"Code complete: {_codes[0]}." : $"{Number(_codes[0].Length)} of 6 entered."
                    ]
                ],
                Ui.Card.Key("form").Class("space-y-8")[
                    Div.Class("mx-auto max-w-64 space-y-2")[
                        Ui.Heading.Lg.Class("text-center")["Verify your account"],
                        Ui.Text.Class("text-center")["Please enter a one-time password from the authenticator app."]
                    ],
                    Code(1).Length(6).Label("OTP Code").Class("mx-auto"),
                    Div.Class("space-y-4")[
                        Ui.Button.Key("verify").Primary.Class("w-full").OnClick(() => { _verified = _codes[1]; })["Verify"],
                        Ui.Button.Key("resend").Class("w-full").Data(Testid("ui-otp-resend"))
                            .OnClick(() => { _codes[1] = ""; })["Resend code"]
                    ]
                ],
                Div.Key("auto").Class("space-y-2")[
                    Code(2).Length(6).OnComplete(code => { _verified = code; }).Class("mx-auto"),
                    P.Class("text-center text-sm text-ui-muted").Data(Testid("ui-otp-verified"))[
                        _verified is null ? "Fill every cell to verify." : $"Verifying {_verified}…"
                    ]
                ],
                Div.Key("license").Class("sm:col-span-2")[
                    Code(3).Length(10).Alphanumeric.Autocomplete("off").Label("License key")
                        .DescriptionTrailing("Enter the license key printed on the installation disc"),
                    P.Class("mt-2 text-sm text-ui-muted").Data(Testid("ui-otp-license"))[$"Key: {_codes[3]}"]
                ],
                Code(4).Key("pin").Length(4).Private().Label("PIN Code")
            ]);

    private Component OneTimeCodeLayoutSection() =>
        Section(
            "OTP input — separators and groups",
            "Place the cells yourself to put a separator between them, or join them into groups.",
            Div.Data(Testid("ui-otp-layout")).Class("flex flex-wrap items-center gap-8")[
                Code(5).Key("separator")[
                    Ui.OtpInput, Ui.OtpInput, Ui.OtpInput,
                    Ui.OtpSeparator,
                    Ui.OtpInput, Ui.OtpInput, Ui.OtpInput
                ],
                Code(6).Key("group")[
                    Ui.OtpGroup[Ui.OtpInput, Ui.OtpInput, Ui.OtpInput, Ui.OtpInput, Ui.OtpInput, Ui.OtpInput]
                ],
                Code(7).Key("groups")[
                    Ui.OtpGroup[Ui.OtpInput, Ui.OtpInput, Ui.OtpInput],
                    Ui.OtpSeparator,
                    Ui.OtpGroup[Ui.OtpInput, Ui.OtpInput, Ui.OtpInput]
                ]
            ]);

    // A controlled slider keeps its value in the page: the thumb is drawn from it, so each example owns one.
    private UiSlider<int> Slide(int example) =>
        Ui.Slider.Value(_slides[example]).OnChange(v => { _slides[example] = v; });

    private UiOtp Code(int example) =>
        Ui.Otp.Value(_codes[example]).OnChange(v => { _codes[example] = v; });

    private static IEnumerable<Component> Steps(Func<int, Component> tick) =>
        Enumerable.Range(1, 5).Select(tick);

    private static string Number(int value) =>
        value.ToString(System.Globalization.CultureInfo.InvariantCulture);

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

    private Component CalendarSection() =>
        Section(
            "Calendar",
            "Built in C#, because the element daisyUI styles for this is a JavaScript web component the "
            + "kit does not ship. Every day is a button carrying its full date as its name.",
            Div.Data(Testid("ui-calendar")).Class("space-y-2")[
                // Month and OnMonth are the VIEW, not the value: paging through months changes nothing
                // a form would submit, which is why they are not part of the binding.
                Ui.Calendar
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
            ]);

    private Component SeveralDaysRangeSection() =>
        Section(
            "Several days, a range, and a picker",
            "The same entries. Bind a collection of days and Ui.Calendar picks several; bind a UiDateRange and it "
            + "picks a range — the first click is held and drawn, the second writes the whole range, so the model "
            + "never holds half of one. Ui.DatePicker is the field-shaped button that opens the grid in a popover: "
            + "a single day closes it on the pick, several days keep it open, a range closes on its second click.",
            Div.Data(Testid("ui-dates")).Class("grid gap-4 md:grid-cols-2")[
                Div.Class("space-y-2")[
                    Ui.Calendar.Value(_stay).Label("Stay").Class("max-w-xs")
                        .OnChange(r => { _stay = r; }),
                    P.Class("text-sm text-ui-muted").Data(Testid("ui-dates-stay"))[
                        _stay == default
                            ? "No stay chosen."
                            : $"Stay: {Iso(_stay.Start)} to {Iso(_stay.End)}"
                    ]
                ],
                Div.Class("space-y-3")[
                    Ui.DatePicker.Value(_arrival).Label("Arrival")
                        .OnChange(d => { _arrival = d; }),
                    Ui.DatePicker.Values(_daysOff).Label("Days off")
                        .OnChange(days => { _daysOff = [.. days]; }),
                    Ui.DatePicker.Value(_stay).Label("Stay, as a field")
                        .OnChange(r => { _stay = r; }),
                    P.Class("text-sm text-ui-muted").Data(Testid("ui-dates-picked"))[
                        _arrival == default ? "No arrival chosen." : $"Arrival: {Iso(_arrival)}",
                        $" · {_daysOff.Count} days off"
                    ]
                ]
            ]);

    private Component FileDropAreaSection() =>
        Section(
            "File drop area",
            "Still the native file input, stretched invisibly over the whole area: a click anywhere opens the "
            + "picker and a file dropped anywhere lands in the input, which the browser already does with no "
            + "script. The runtime only marks the area while a file is dragged over it.",
            Div.Data(Testid("ui-dropzone")).Class("max-w-md space-y-2")[
                Ui.FileInput.Value("").Key("receipts").Label("Receipts").Id("demo-receipts")
                    .Dropzone()
                    .Title("Drop receipts here, or click to choose")
                    .Text("PDF or JPG, several at once")
                    .Accept(".pdf,.jpg,.jpeg")
                    .Multiple()
                    .OnFiles(files =>
                    {
                        _dropped.Clear();
                        _dropped.AddRange(files.Select(f => f.Name));
                    }),
                P.Class("text-sm text-ui-muted").Data(Testid("ui-dropzone-state"))[
                    _dropped.Count == 0 ? "No files yet." : "Chosen: " + string.Join(", ", _dropped)
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

    private static string Iso(DateOnly date) =>
        date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

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
