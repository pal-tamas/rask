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
    private double _volume = 40;
    private int _stars = 4;
    private DateOnly _month = DateOnly.FromDateTime(DateTime.Today);
    private DateOnly? _date;

    /// <inheritdoc />
    protected override Component? Render() =>
    [
        Section(
            "Text controls",
            "Tone colours the border — which is how a field says it is in error without a second "
            + "element — and Ghost is the borderless form. daisyUI defines no outline, soft or dash for "
            + "a text control, so those draw the default rather than a class that does nothing.",
            Div.Data(Testid("ui-text-controls")).Class("grid gap-3 sm:grid-cols-2")[
                UiInput.Key("email").Label("Email").Type(InputType.Email).Value(_email)
                    .Placeholder("you@example.com")
                    .Tone(_email.Length > 0 && !_email.Contains('@') ? UiTone.Error : null)
                    .OnChange(v => { _email = v; }),
                UiInput.Key("ghost").Label("Search").Variant(UiVariant.Ghost).Placeholder("Ghost"),
                UiTextarea.Key("notes").Label("Notes").Rows(3).Value(_notes).Placeholder("Anything else?")
                    .OnChange(v => { _notes = v; }),
                // Data-shaped rather than children: the options are (value, text) pairs, so the
                // component owns the <option> markup and the empty placeholder row.
                UiSelect.Key("country").Label("Country")
                    .Options([("hu", "Hungary"), ("gb", "United Kingdom")])
                    .Placeholder("Choose…"),
                UiFileInput.Key("avatar").Label("Avatar").Size(UiSize.Sm),
                _email.Length > 0 && !_email.Contains('@')
                    ? UiValidator.Key("v").Message("That does not look like an email address.")
                    : null
            ]),

        Section(
            "Labels",
            "A caption inside the field's own frame, and one that rises out of the way when there is "
            + "content. Both are decoration: the control keeps its own accessible name.",
            Div.Data(Testid("ui-labels")).Class("grid gap-3 sm:grid-cols-2")[
                UiLabel.Key("price").Text("€").Trailing("per month")[
                    UiInput.Label("Price").Placeholder("29")
                ],
                UiFloatingLabel.Key("float").Text("Company")[
                    UiInput.Label("Company").Placeholder("Company")
                ]
            ]),

        Section(
            "Choices",
            "The words are part of the hit target: on a phone a 16px box on its own is the difference "
            + "between a control and a dare.",
            Div.Data(Testid("ui-choices")).Class("flex flex-wrap items-center gap-4")[
                UiCheckbox.Key("remember").Text("Remember me").Checked(_remember).Tone(UiTone.Primary)
                    .OnChange(v => { _remember = v; }),
                UiToggle.Key("alerts").Text("Email alerts").Checked(_alerts).Tone(UiTone.Success)
                    .OnChange(v => { _alerts = v; }),
                UiRadio.Key("std").Text("Standard").Group("shipping").Checked(_shipping == "standard")
                    .OnSelected(() => { _shipping = "standard"; }),
                UiRadio.Key("exp").Text("Express").Group("shipping").Checked(_shipping == "express")
                    .OnSelected(() => { _shipping = "express"; })
            ]),

        Section(
            "Range and rating",
            "A range can stand on end, and daisyUI puts the low value at the bottom — which is what a "
            + "volume wants and what a rank does not.",
            Div.Data(Testid("ui-range")).Class("grid max-w-sm gap-4")[
                UiRange.Key("vol").Label("Volume").Value(_volume).Min(0).Max(100).Step(5)
                    .Tone(UiTone.Accent).OnChange(v => { _volume = v; }),
                Span.Class("text-sm text-ui-muted")[
                    $"Volume: {_volume.ToString("0", System.Globalization.CultureInfo.InvariantCulture)}"
                ],
                UiRating.Key("stars").Group("score").Label("Rate this").Value(_stars).Max(5)
                    .OnChange(v => { _stars = v; })
            ]),

        Section(
            "One-time code",
            "One input drawn as several. Per-digit boxes need script to move focus, defeat the "
            + "browser's SMS autofill, and drop a pasted code entirely into the first box.",
            Div.Data(Testid("ui-otp")).Class("space-y-2")[
                UiOtp.Key("otp").Label("Verification code").Length(6).Value(_code).Joined(true)
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
                UiFilter.Key("tags").Group("demo-tags").Options(["bug", "feature", "docs"])
                    .Selected(_tag).ResetLabel("All").OnSelect(tag => { _tag = tag; }),
                P.Class("text-sm text-ui-muted").Data(Testid("ui-filter-state"))[
                    _tag is null ? "Showing everything." : $"Filtered to {_tag}."
                ]
            ]),

        Section(
            "Calendar",
            "Built in C#, because the element daisyUI styles for this is a JavaScript web component the "
            + "kit does not ship. Every day is a button carrying its full date as its name.",
            Div.Data(Testid("ui-calendar")).Class("space-y-2")[
                UiCalendar
                    .Label("Delivery date")
                    .Month(_month)
                    .OnMonth(m => { _month = m; })
                    .Selected(_date)
                    .OnSelect(d => { _date = d; })
                    .Class("max-w-xs"),
                P.Class("text-sm text-ui-muted").Data(Testid("ui-calendar-state"))[
                    _date is { } picked
                        ? $"Chosen: {picked.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)}"
                        : "No date chosen."
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
}
