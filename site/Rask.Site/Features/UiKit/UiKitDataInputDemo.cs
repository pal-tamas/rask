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
    private double _volume = 40;
    private int _stars = 4;
    private DateOnly _month = DateOnly.FromDateTime(DateTime.Today);
    private DateOnly? _date;
    private readonly Signup _signup = new();

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
                UiInput.Value(_email).Key("email").Label("Email").Type(InputType.Email)
                    .Placeholder("you@example.com")
                    .Tone(_email.Length > 0 && !_email.Contains('@') ? UiTone.Error : (UiTone?)null)
                    .OnChange(v => { _email = v; }),
                UiInput.Of<string>().Key("ghost").Label("Search").Variant(UiVariant.Ghost)
                    .Placeholder("Ghost"),
                UiTextarea.Value(_notes).Key("notes").Label("Notes").Rows(3)
                    .Placeholder("Anything else?")
                    .OnChange(v => { _notes = v; }),
                UiSelect.Value(_country).Key("country")
                    .Options([("hu", "Hungary"), ("gb", "United Kingdom")])
                    .Label("Country")
                    .Placeholder("Choose…")
                    .OnChange(v => { _country = v; }),
                UiFileInput.Value("").Key("avatar").Label("Avatar").Size(UiSize.Sm),
                _email.Length > 0 && !_email.Contains('@')
                    ? UiValidator.Key("v").Message("That does not look like an email address.")
                    : null
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
            "Labels",
            "A caption inside the field's own frame, and one that rises out of the way when there is "
            + "content. Both are decoration: the control keeps its own accessible name.",
            Div.Data(Testid("ui-labels")).Class("grid gap-3 sm:grid-cols-2")[
                UiLabel.Key("price").Text("€").Trailing("per month")[
                    UiInput.Of<string>().Label("Price").Placeholder("29")
                ],
                UiFloatingLabel.Key("float").Text("Company")[
                    UiInput.Of<string>().Label("Company").Placeholder("Company")
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
            "Bound to a model",
            "The same controls, with no OnChange between them and the model. Bind is the other opening "
            + "of the same chain: it two-way binds, drives the surrounding Form's per-field validation, "
            + "and the control writes back itself.",
            Div.Data(Testid("ui-bound")).Class("space-y-3")[
                Form.Model(_signup)[
                    Div.Class("grid gap-3 sm:grid-cols-2")[
                        UiInput.Bind(() => _signup.Email).Label("Email").Type(InputType.Email)
                            .Placeholder("you@example.com"),
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

    private static Component Masked(string key, UiMaskShape shape) =>
        UiMask.Key(key).Shape(shape).Class("size-14 bg-primary");

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
