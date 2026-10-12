using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary>
///     <c>fluxui.dev/components/popover</c>, example for example, as the page loads: every popover closed.
/// </summary>
/// <remarks>
///     <para>
///     The component is the OPEN panel, which a page as loaded never shows: <see cref="PopoverOpenParity" />
///     writes these same examples once per dropdown, with that dropdown's trigger marked for the measurer to
///     open, and <c>scripts/flux/parity-popover.mjs</c> holds each to Flux's.
///     </para>
///     <para>
///     What a panel holds is other pages' components, and they are the real ones. The three grey lines in the
///     position and gap examples are the docs page's own filler, and the two bare buttons (the hover card's
///     and the event's) are written in the example itself: each is a box of Flux's size.
///     </para>
/// </remarks>
public sealed partial class PopoverParity : FluxParity
{
    private const string Caleb = "https://unavatar.io/x/calebporzio";

    // What an app's own Tailwind build emits for the classes these examples hand to `class` and the kit's
    // sheet does not carry, each under a name of its own. In `utilities`, where Tailwind puts them: outside
    // every layer they would beat the rule that keeps a closed panel from being displayed. And room under the
    // last example, as Flux's page has: a panel opens downwards only where there is room for it.
    private const string AppUtilities =
        "<style>body{padding-bottom:900px}@layer utilities{.pp-max-w-18{max-width:18rem}.pp-min-w-30{min-width:30rem}.pp-w-28{width:7rem}"
        + ".pp-shadow-xl{--tw-shadow:0 20px 25px -5px var(--tw-shadow-color,#0000001a),0 8px 10px -6px var(--tw-shadow-color,#0000001a);"
        + "box-shadow:var(--tw-inset-shadow),var(--tw-inset-ring-shadow),var(--tw-ring-offset-shadow),var(--tw-ring-shadow),var(--tw-shadow)}"
        // `justify-start -m-2 px-2!` on the two subtle buttons at the foot of a panel.
        + ".pp-foot{justify-content:flex-start;margin:-.5rem;padding-inline:.5rem!important}"
        // `icon:class`, `icon-trailing:class` and `label:class`: classes Flux's props hand to a part.
        + ".pp-icons-400>[data-ui-icon]{color:var(--color-zinc-400)}.pp-icons-300>[data-ui-icon]{color:var(--color-zinc-300)}"
        + ".pp-icons-75>[data-ui-icon]{opacity:.75}"
        + ".pp-icons-quiet>[data-ui-icon]{color:var(--color-zinc-400)}:where(.dark) .pp-icons-quiet>[data-ui-icon]{color:var(--color-zinc-300)}"
        + ".pp-labels>[data-ui-field]>[data-ui-label]{color:var(--color-zinc-500)}"
        + ":where(.dark) .pp-labels>[data-ui-field]>[data-ui-label]{color:var(--color-zinc-400)}"
        + ".pp-clear:where(.dark,.dark *){background-color:transparent!important}"
        // What the live example adds for its dark panel and the markdown leaves out: the trigger's bar, and
        // the avatars ringed in the panel's own zinc-700.
        + ".pp-bar{background-color:var(--color-zinc-800)}:where(.dark) .pp-bar{background-color:var(--color-zinc-200)}"
        + ":where(.dark) .pp-rings>*{--tw-ring-color:var(--color-zinc-700)}}</style>";

    /// <summary>How many dropdowns the page holds, across its seven examples.</summary>
    internal const int Dropdowns = 11;

    private static readonly string[] Sections = ["", "hover-trigger", "position", "gap-&-offset", "examples", "examples", "examples"];

    public override string Page => "popover";

    public override IEnumerable<(string Section, Component Example)> Examples() =>
        Sections.Select((section, example) => (section, Example(example, opened: -1)));

    /// <summary>The example each dropdown sits in, in page order.</summary>
    internal static int ExampleOf(int dropdown) => dropdown switch
    {
        0 => 0,
        1 => 1,
        <= 5 => 2,
        <= 7 => 3,
        _ => dropdown - 4,
    };

    /// <summary>
    ///     One of the page's seven examples. <paramref name="opened" /> is the dropdown — counted across the
    ///     page — whose trigger is marked for the measurer to open, or -1 for none.
    /// </summary>
    internal static Component Example(int example, int opened) => example switch
    {
        0 => Div[Raw.Value(AppUtilities), Row(Options(opened == 0))],
        1 => Div[Raw.Value(AppUtilities), Row(HoverCard(opened == 1))],
        2 => Grid(320,
            Ui.Dropdown.Top.Start[Placed("Top Start", opened == 2), Ui.Popover[Lines()]],
            Ui.Dropdown.Right.Center[Placed("Right Center", opened == 3), Ui.Popover[Lines()]],
            // Flux's own example: the three lines are set against the panel's far edge here.
            Ui.Dropdown.Left.Center[Placed("Left Center", opened == 4), Ui.Popover[Lines("margin-left:auto")]],
            Ui.Dropdown.Bottom.End[Placed("Bottom End", opened == 5), Ui.Popover[Lines()]]),
        3 => Grid(384,
            Ui.Dropdown.Gap(16)[Placed("Gap: 16px", opened == 6), Ui.Popover[Lines()]],
            Ui.Dropdown.Offset(32)[Placed("Offset: 32px", opened == 7), Ui.Popover[Lines()]]),
        4 => Div[Raw.Value(AppUtilities), Row(Div[Categories(opened == 8)])],
        5 => Div[Raw.Value(AppUtilities), Row(Div[Feedback(opened == 9)])],
        _ => Div[Raw.Value(AppUtilities), Row(Div[EventDetails(opened == 10)])],
    };

    private static Component Options(bool opened) =>
        Ui.Dropdown[
            Opens(Ui.Button.Icon(Ui.IconName.AdjustmentsHorizontal).IconTrailing(Ui.IconName.ChevronDown).Class("pp-icons-400"), opened)["Options"],
            Ui.Popover.Class("flex flex-col gap-4 pp-labels")[
                Ui.RadioGroup.Value("posted").Id("sort").Label("Sort by")[
                    Ui.Radio.Value("active").Label("Recently active"),
                    Ui.Radio.Value("posted").Label("Date posted")
                ],
                Ui.Separator.Subtle,
                Ui.RadioGroup.Value("list").Id("view").Label("View as")[
                    Ui.Radio.Value("list").Label("List"),
                    Ui.Radio.Value("gallery").Label("Gallery")
                ],
                Ui.Separator.Subtle,
                Ui.Button.Subtle.Sm.Class("pp-foot")["Reset to default"]
            ]
        ];

    private static Component HoverCard(bool opened) =>
        Ui.Dropdown.Hover().Bottom.Start.Offset(-16).Gap(10)[
            // The example's own <button class="flex items-center gap-3">.
            Opens(Button.Type(ButtonType.Button).Style("display:flex;align-items:center;gap:12px").Data("parity-skip", "self"), opened, hover: true)[
                Ui.Avatar.Sm.Name("Caleb Porzio").Src(Caleb),
                Ui.Heading["Caleb Porzio"]
            ],
            Ui.Popover.Class("flex flex-col gap-3 rounded-xl pp-shadow-xl")[
                Ui.Avatar.Xl.Name("Caleb Porzio").Src(Caleb),
                Div[
                    Ui.Heading.Lg["Caleb Porzio"],
                    Div.Style("display:flex;align-items:center;gap:8px")[
                        Ui.Text.Lg["@calebporzio"],
                        Ui.Badge["Follows you"]
                    ]
                ],
                // Flux's example sets a heading inside a text — a <div> inside a <p>, which the browser
                // closes the paragraph for. Written the same way, so it parses the same way.
                Div.Style("display:flex;align-items:center;gap:16px")[
                    Ui.Text.Class("flex gap-1")[Ui.Heading["775"], " following"],
                    Ui.Text.Class("flex gap-1")[Ui.Heading["50.2k"], " followers"]
                ],
                Div.Style("display:flex;gap:8px")[
                    Ui.Button.Sm.Icon(Ui.IconName.Check).Class("flex-1 pp-icons-75")["Following"],
                    Ui.Button.Primary.Sm.Icon(Ui.IconName.ChatBubbleLeftRight).Class("flex-1 pp-icons-75")["Message"]
                ]
            ]
        ];

    private static Component Categories(bool opened) =>
        Ui.Dropdown[
            // Flux's button with a badge in its `iconTrailing` SLOT. Ui.Button's IconTrailing names an icon, so
            // this trigger is a box of Flux's size: the panel is placed against it.
            Opens(DropdownParity.Room("width:150.8px;height:40px"), opened),
            Ui.Popover.Class("pp-max-w-18 flex flex-col gap-4")[
                Ui.CheckboxGroup.Values(["fantasy", "science-fiction", "thriller"]).Id("picked").Variant(Ui.CheckboxGroupVariant.Pills)[
                    Ui.Checkbox.Value("fantasy").Label("Fantasy"),
                    Ui.Checkbox.Value("science-fiction").Label("Science fiction"),
                    Ui.Checkbox.Value("horror").Label("Horror"),
                    Ui.Checkbox.Value("mystery").Label("Mystery"),
                    Ui.Checkbox.Value("romance").Label("Romance"),
                    Ui.Checkbox.Value("autobiography").Label("Autobiography"),
                    Ui.Checkbox.Value("thriller").Label("Thriller"),
                    Ui.Checkbox.Value("poetry").Label("Poetry"),
                    Ui.Checkbox.Value("children").Label("Children")
                ],
                Ui.Separator.Subtle,
                // `wire:click` on Flux's button: it waits on its handler, so it carries the loading indicator.
                Ui.Button.Subtle.Sm.Class("pp-foot").OnClick(() => { })["Clear all"]
            ]
        ];

    private static Component Feedback(bool opened) =>
        Ui.Dropdown[
            Opens(Ui.Button.Icon(Ui.IconName.ChatBubbleOvalLeft).Class("pp-icons-300"), opened)["Feedback"],
            Ui.Popover.Class("pp-min-w-30 flex flex-col gap-4")[
                Ui.RadioGroup.Value("bug").Id("kind").Buttons[
                    Ui.Radio.Value("bug").Icon(Ui.IconName.BugAnt).Class("flex-1")["Bug report"],
                    Ui.Radio.Value("suggestion").Icon(Ui.IconName.LightBulb).Class("flex-1")["Suggestion"],
                    Ui.Radio.Value("question").Icon(Ui.IconName.QuestionMarkCircle).Class("flex-1")["Question"]
                ],
                Div.Style("position:relative")[
                    Ui.Textarea.Of<string>().Rows(8).Class("pp-clear")
                        .Placeholder("Please include reproduction steps. You may attach images or video files."),
                    Div.Style("position:absolute;bottom:12px;left:12px;display:flex;align-items:center;gap:8px")[
                        Ui.Button.Filled.Xs.Icon(Ui.IconName.FaceSmile).IconVariant(Ui.IconVariant.Outline).Class("pp-icons-quiet"),
                        Ui.Button.Filled.Xs.Icon(Ui.IconName.PaperClip).Class("pp-icons-quiet")
                    ]
                ],
                Div.Style("display:flex;gap:8px;justify-content:flex-end")[
                    Ui.Button.Filled.Sm.Kbd("esc").Class("pp-w-28")["Cancel"],
                    Ui.Button.Sm.Kbd("⏎").Class("pp-w-28")["Submit"]
                ]
            ]
        ];

    private static Component EventDetails(bool opened)
    {
        // The example's own <button class="w-54 rounded-lg p-2 flex items-center gap-2 bg-zinc-100 hover:bg-zinc-200">.
        var trigger = Opens(
            Button.Type(ButtonType.Button).Data("parity-skip", "self")
                .Style("width:13.5rem;border-radius:8px;padding:8px;display:flex;align-items:center;gap:8px;background:var(--color-zinc-100)"),
            opened);
        // `data-open:flex` is said by the panel's own mark, which the page writes once it has heard the
        // toggle. A static page hears nothing, so the dropdown that is measured open is rendered as open.
        var dropdown = opened ? Ui.Dropdown.Bottom.Center.Open(true) : Ui.Dropdown.Bottom.Center;

        return dropdown[
            trigger[
                Div.Class("pp-bar").Style("align-self:stretch;width:2px;border-radius:calc(infinity * 1px)"),
                Div[Ui.Heading["Team sync"], Ui.Text.Class("mt-1")["10:00 AM"]]
            ],
            Ui.Popover.Class("w-80 p-0 data-open:flex flex-col")[
                Div.Style("padding:16px")[
                    Ui.Heading["Team sync"],
                    Ui.Text.Class("mt-2")["Let's review the progress made last week and define the priorities for the next"],
                    Ui.RadioGroup.Value("going").Id("answer").Segmented.Class("mt-3")[
                        Ui.Radio.Value("going").Label("Going"),
                        Ui.Radio.Value("not-going").Label("Not going"),
                        Ui.Radio.Value("maybe").Label("Maybe")
                    ]
                ],
                Ui.Separator,
                Block(Ui.IconName.Users,
                    Between(Div.Style("display:flex;gap:8px")[Ui.Heading["Guests"]], "Invite"),
                    Div.Style("display:flex;align-items:center;gap:12px;margin-top:8px")[
                        Ui.AvatarGroup.Class("pp-rings")[
                            Ui.Avatar.Xs.Circle().Src(Caleb),
                            Ui.Avatar.Xs.Circle().Src("https://unavatar.io/github/hugosaintemarie"),
                            Ui.Avatar.Xs.Circle().Src("https://unavatar.io/github/joshhanley")
                        ],
                        Ui.Text["1 maybe, 1 awaiting"]
                    ]),
                Ui.Separator,
                Block(Ui.IconName.Clock,
                    Between(
                        Div.Style("display:flex;gap:8px")[
                            Ui.Heading["10:00 AM"],
                            Ui.Icon.Name(Ui.IconName.ArrowRight).Micro.Class("text-zinc-400"),
                            Ui.Heading["11:00 AM"]
                        ],
                        "Edit"),
                    Div.Style("display:flex;gap:12px;margin-top:8px")[
                        Ui.Text["May 29 2025"],
                        Ui.Text["·"],
                        Ui.Text.Class("flex items-center gap-1.5")[
                            Ui.Icon.Name(Ui.IconName.ArrowPathRoundedSquare).Micro.Class("text-zinc-400"),
                            "Every weekday"
                        ]
                    ]),
                Ui.Separator,
                Block(Ui.IconName.MapPin,
                    Between(Ui.Heading["Location"], "Edit"),
                    Ui.Text.Class("mt-2")["Paris HQ, 13 rue de la Prairie 75018"])
            ]
        ];
    }

    // One of the event's three blocks: an icon, and beside it a heading row over a line of detail.
    private static Component Block(Ui.IconName icon, Component heading, Component detail) =>
        Div.Style("padding:16px;display:flex;gap:8px")[
            Ui.Icon.Name(icon).Micro.Class("text-zinc-400"),
            Div.Style("flex:1 1 0%")[heading, detail]
        ];

    private static Component Between(Component start, string link) =>
        Div.Style("display:flex;align-items:center;justify-content:space-between")[
            start,
            Ui.Text[Ui.Link.Href("#").Variant(Ui.LinkVariant.Subtle)[link]]
        ];

    // The trigger of a position or gap example: Flux's small button, as wide as its cell.
    private static Component Placed(string label, bool opened) => Opens(Ui.Button.Filled.Sm.Class("w-full"), opened)[label];

    // The three placeholder lines Flux's docs fill those panels with: 8px tall, 8px apart.
    private static Component[] Lines(string? align = null) =>
    [
        Div.Data("parity-skip", "").Style("width:96px;height:8px;margin-bottom:8px;" + align),
        Div.Data("parity-skip", "").Style("width:128px;height:8px;margin-bottom:8px;" + align),
        Div.Data("parity-skip", "").Style("width:80px;height:8px;" + align)
    ];

    private static Component Grid(int width, params Component[] dropdowns) =>
        Div[
            Raw.Value(AppUtilities),
            Div.Style(FormattableString.Invariant($"display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:16px;width:{width}px;margin:0 auto"))[dropdowns]
        ];

    // Marks what the measurer presses — or hovers — to open this dropdown.
    private static T Opens<T>(T trigger, bool opened, bool hover = false)
        where T : Element
    {
        if (!opened)
        {
            return trigger;
        }

        var data = new Dictionary<string, string?>(StringComparer.Ordinal) { ["parity-open"] = hover ? "hover" : "" };
        foreach (var (name, value) in trigger.Data ?? new Dictionary<string, string?>(StringComparer.Ordinal))
        {
            data[name] = value;
        }

        return trigger.Data(data);
    }
}
