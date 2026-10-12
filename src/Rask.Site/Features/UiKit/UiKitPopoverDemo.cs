using System.Globalization;

namespace Rask.Site.Features.UiKit;

/// <summary>
///     Flux UI's popover page, example for example: a <c>Ui.Dropdown</c> around a trigger and a <c>Ui.Popover</c>.
/// </summary>
public sealed partial class UiKitPopoverDemo : Component
{
    private static readonly (string Value, string Label)[] Categories =
    [
        ("fantasy", "Fantasy"), ("science-fiction", "Science fiction"), ("horror", "Horror"), ("mystery", "Mystery"),
        ("romance", "Romance"), ("autobiography", "Autobiography"), ("thriller", "Thriller"), ("poetry", "Poetry"),
        ("children", "Children"),
    ];

    private string _sort = "posted";
    private string _view = "list";
    private List<string> _picked = ["fantasy", "science-fiction", "thriller"];
    private string _kind = "bug";
    private string _feedback = "";
    private bool _feedbackOpen;
    private string _answer = "going";
    private string _sent = "nothing yet";

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Data(Testid("ui-popover"))[
            Section("Popover",
                "Extra content in a panel beside a button — for what is not a menu. The dropdown holds the trigger and "
                + "the popover; the browser opens it, closes it on Escape and on a press anywhere else, and focus stays "
                + "on the trigger, from where Tab walks in. The page behind neither scrolls nor takes the pointer.",
                Row("options", Options(), Span.Key("says").Data(Testid("ui-popover-says")).Class("text-sm text-ui-muted")[
                    $"Sorted by {_sort}, viewed as {_view}."
                ])),
            Section("Hover trigger", "Hover() on the dropdown: the pointer opens it and leaving closes it. A press and Enter open it too.",
                Row("hover", HoverCard())),
            Section("Position", "Position and Align are the dropdown's: the side the panel opens on, and where along it.",
                Div.Data(Testid("ui-popover-position")).Class("grid max-w-xs grid-cols-2 gap-4")[
                    Placed("ts", Ui.Dropdown.Top.Start, "Top Start"),
                    Placed("rc", Ui.Dropdown.Right.Center, "Right Center"),
                    Placed("lc", Ui.Dropdown.Left.Center, "Left Center"),
                    Placed("be", Ui.Dropdown.Bottom.End, "Bottom End")
                ]),
            Section("Gap & offset", "Gap is the distance from the trigger; Offset shifts the panel along its edge.",
                Div.Data(Testid("ui-popover-gap")).Class("grid max-w-sm grid-cols-2 gap-4")[
                    Placed("gap", Ui.Dropdown.Gap(16), "Gap: 16px"),
                    Placed("offset", Ui.Dropdown.Offset(32), "Offset: 32px")
                ]),
            Section("Category picker", "A list of selectable categories, bound to the page.",
                Row("categories", CategoryPicker())),
            Section("Feedback",
                "A form in a panel. The page owns Open here, so the form's own buttons close it — and OnToggle is how the "
                + "page hears Escape and a press outside.",
                Row("feedback", Feedback(), Span.Key("sent").Data(Testid("ui-popover-sent")).Class("text-sm text-ui-muted")[
                    $"Last feedback: {_sent}."
                ])),
            Section("Event details", "Detailed information about an event, in a panel with no padding of its own.",
                Row("event", EventDetails()))
        ];

    private Component Options() =>
        Ui.Dropdown.Key("options")[
            Ui.Button.Icon(Ui.IconName.AdjustmentsHorizontal).IconTrailing(Ui.IconName.ChevronDown)["Options"],
            Ui.Popover.Class("flex flex-col gap-4")[
                Ui.RadioGroup.Value(_sort).Key("sort").Id("popover-sort").Label("Sort by").OnChange(v => { _sort = v; })[
                    Ui.Radio.Value("active").Label("Recently active"),
                    Ui.Radio.Value("posted").Label("Date posted")
                ],
                Ui.Separator.Key("s1").Subtle,
                Ui.RadioGroup.Value(_view).Key("view").Id("popover-view").Label("View as").OnChange(v => { _view = v; })[
                    Ui.Radio.Value("list").Label("List"),
                    Ui.Radio.Value("gallery").Label("Gallery")
                ],
                Ui.Separator.Key("s2").Subtle,
                Ui.Button.Key("reset").Subtle.Sm.Class("justify-start -m-2 px-2!")
                    .OnClick(() => { _sort = "posted"; _view = "list"; })["Reset to default"]
            ]
        ];

    private static Component HoverCard() =>
        Ui.Dropdown.Key("hover").Hover().Bottom.Start.Offset(-16).Gap(10)[
            Button.Type(ButtonType.Button).Class("flex items-center gap-3")[
                Ui.Avatar.Sm.Name("Caleb Porzio"),
                Ui.Heading["Caleb Porzio"]
            ],
            Ui.Popover.Class("flex flex-col gap-3 rounded-xl shadow-xl")[
                Ui.Avatar.Key("face").Xl.Name("Caleb Porzio"),
                Div.Key("who")[
                    Ui.Heading.Lg["Caleb Porzio"],
                    Div.Class("flex items-center gap-2")[Ui.Text.Lg["@calebporzio"], Ui.Badge["Follows you"]]
                ],
                Div.Key("counts").Class("flex items-center gap-4")[
                    Div.Class("flex gap-1")[Ui.Heading["775"], Ui.Text["following"]],
                    Div.Class("flex gap-1")[Ui.Heading["50.2k"], Ui.Text["followers"]]
                ],
                Div.Key("actions").Class("flex gap-2")[
                    Ui.Button.Key("following").Sm.Icon(Ui.IconName.Check).Class("flex-1")["Following"],
                    Ui.Button.Key("message").Primary.Sm.Icon(Ui.IconName.ChatBubbleLeftRight).Class("flex-1")["Message"]
                ]
            ]
        ];

    private static Component Placed(string key, UiDropdown dropdown, string label) =>
        dropdown.Key(key)[
            Ui.Button.Filled.Sm.Class("w-full")[label],
            Ui.Popover[Ui.Text[label + " of its button."]]
        ];

    private Component CategoryPicker() =>
        Ui.Dropdown.Key("categories")[
            Ui.Button.Icon(Ui.IconName.Tag)[
                "Categories",
                Ui.Badge.Sm.Class("ms-2 -me-1 tabular-nums").Data(Testid("ui-popover-count"))[_picked.Count.ToString(CultureInfo.InvariantCulture)]
            ],
            Ui.Popover.Class("max-w-[18rem] flex flex-col gap-4")[
                Ui.CheckboxGroup.Values(_picked).Key("picked").Id("popover-categories").Variant(Ui.CheckboxGroupVariant.Pills)
                    .OnChange(v => { _picked = [.. v]; })[
                    Categories.Select(category => Ui.Checkbox.Key(category.Value).Value(category.Value).Label(category.Label))
                ],
                Ui.Separator.Key("s").Subtle,
                Ui.Button.Key("clear").Subtle.Sm.Class("justify-start -m-2 px-2!").OnClick(() => { _picked = []; })["Clear all"]
            ]
        ];

    private Component Feedback() =>
        Ui.Dropdown.Key("feedback").Open(_feedbackOpen).OnToggle(open => { _feedbackOpen = open; })[
            Ui.Button.Icon(Ui.IconName.ChatBubbleOvalLeft)["Feedback"],
            Ui.Popover.Class("flex w-[min(30rem,calc(100vw-2rem))] flex-col gap-4")[
                Ui.RadioGroup.Value(_kind).Key("kind").Id("popover-kind").Buttons.OnChange(v => { _kind = v; })[
                    Ui.Radio.Value("bug").Icon(Ui.IconName.BugAnt).Class("flex-1")["Bug report"],
                    Ui.Radio.Value("suggestion").Icon(Ui.IconName.LightBulb).Class("flex-1")["Suggestion"],
                    Ui.Radio.Value("question").Icon(Ui.IconName.QuestionMarkCircle).Class("flex-1")["Question"]
                ],
                Ui.Textarea.Value(_feedback).Key("text").Id("popover-feedback").Rows(8).Class("dark:bg-transparent!")
                    .Placeholder("Please include reproduction steps. You may attach images or video files.")
                    .OnInput(v => { _feedback = v; }),
                Div.Key("actions").Class("flex justify-end gap-2")[
                    Ui.Button.Key("cancel").Filled.Sm.Kbd("esc").Class("w-28").OnClick(() => { _feedbackOpen = false; })["Cancel"],
                    Ui.Button.Key("submit").Sm.Kbd("⏎").Class("w-28").OnClick(Send)["Submit"]
                ]
            ]
        ];

    private void Send()
    {
        _sent = _feedback.Length == 0 ? $"an empty {_kind}" : $"a {_kind}, “{_feedback}”";
        _feedback = "";
        _feedbackOpen = false;
    }

    private Component EventDetails() =>
        Ui.Dropdown.Key("event").Bottom.Center[
            Button.Type(ButtonType.Button)
                .Class("flex w-54 items-center gap-2 rounded-lg bg-zinc-100 p-2 text-start hover:bg-zinc-200 dark:bg-zinc-700 dark:hover:bg-zinc-600")[
                Div.Class("w-0.5 self-stretch rounded-full bg-zinc-800 dark:bg-zinc-200"),
                Div[Ui.Heading["Team sync"], Ui.Text.Class("mt-1")["10:00 AM"]]
            ],
            Ui.Popover.Class("w-80 p-0 data-open:flex flex-col")[
                Div.Key("what").Class("p-4")[
                    Ui.Heading["Team sync"],
                    Ui.Text.Class("mt-2")["Let's review the progress made last week and define the priorities for the next"],
                    Ui.RadioGroup.Value(_answer).Key("answer").Id("popover-answer").Segmented.Class("mt-3").OnChange(v => { _answer = v; })[
                        Ui.Radio.Value("going").Label("Going"),
                        Ui.Radio.Value("not-going").Label("Not going"),
                        Ui.Radio.Value("maybe").Label("Maybe")
                    ]
                ],
                Ui.Separator.Key("s1"),
                Detail("guests", Ui.IconName.Users, Ui.Heading["Guests"],
                    Div.Class("mt-2 flex items-center gap-3")[
                        Ui.AvatarGroup.Class("dark:*:ring-zinc-700")[
                            Ui.Avatar.Xs.Circle().Name("Caleb Porzio"),
                            Ui.Avatar.Xs.Circle().Name("Hugo Sainte-Marie"),
                            Ui.Avatar.Xs.Circle().Name("Josh Hanley")
                        ],
                        Ui.Text["1 maybe, 1 awaiting"]
                    ]),
                Ui.Separator.Key("s2"),
                Detail("when", Ui.IconName.Clock,
                    Div.Class("flex gap-2")[
                        Ui.Heading["10:00 AM"],
                        Ui.Icon.Name(Ui.IconName.ArrowRight).Micro.Class("text-zinc-400"),
                        Ui.Heading["11:00 AM"]
                    ],
                    Div.Class("mt-2 flex gap-3")[
                        Ui.Text["May 29 2025"],
                        Ui.Text["·"],
                        Div.Class("flex items-center gap-1.5")[
                            Ui.Icon.Name(Ui.IconName.ArrowPathRoundedSquare).Micro.Class("text-zinc-400"),
                            Ui.Text["Every weekday"]
                        ]
                    ]),
                Ui.Separator.Key("s3"),
                Detail("where", Ui.IconName.MapPin, Ui.Heading["Location"], Ui.Text.Class("mt-2")["Paris HQ, 13 rue de la Prairie 75018"])
            ]
        ];

    // One block of the event: an icon, and beside it a heading over a line of detail.
    private static Component Detail(string key, Ui.IconName icon, Component heading, Component detail) =>
        Div.Key(key).Class("flex gap-2 p-4")[
            Ui.Icon.Name(icon).Micro.Class("text-zinc-400"),
            Div.Class("flex-1")[heading, detail]
        ];

    private static Component Row(string name, params IEnumerable<Component?> items) =>
        Div.Data(Testid("ui-popover-" + name)).Class("flex flex-wrap items-center gap-4")[items];

    private static AttrBag Testid(string value) => new("testid", value);

    private static Component Section(string heading, string blurb, Component body) =>
        Div.Key(heading).Class("mb-8")[
            H2.Class("text-lg font-semibold tracking-tight")[heading],
            P.Class("mt-1 mb-3 text-sm text-ui-muted")[blurb],
            body
        ];
}
