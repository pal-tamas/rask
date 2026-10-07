namespace Rask.Site.Features.UiKit;

/// <summary>
///     Flux UI's button page, example for example, drawn with <c>Ui.Button</c> and <c>Ui.ButtonGroup</c>.
/// </summary>
public sealed partial class UiKitButtonDemo : Component
{
    private static readonly Ui.Color[] Hues =
    [
        Ui.Color.Red, Ui.Color.Orange, Ui.Color.Amber, Ui.Color.Yellow, Ui.Color.Lime, Ui.Color.Green,
        Ui.Color.Emerald, Ui.Color.Teal, Ui.Color.Cyan, Ui.Color.Sky, Ui.Color.Blue, Ui.Color.Indigo,
        Ui.Color.Violet, Ui.Color.Purple, Ui.Color.Fuchsia, Ui.Color.Pink, Ui.Color.Rose,
    ];

    private int _saves;
    private int _steps;

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Data(Testid("ui-button"))[
            ButtonSection(),
            VariantsSection(),
            ColorsSection(),
            OutlineColorsSection(),
            SizesSection(),
            IconsSection(),
            LoadingSection(),
            FullWidthSection(),
            ButtonGroupsSection(),
            AsALinkSection(),
            SquareSection(),
            InsetSection()
        ];

    private static Component ButtonSection() =>
        Section("Button", "Flux's default is the outline: a white surface, a hairline border, a soft shadow.",
            Row("intro", Ui.Button["Button"]));

    private static Component VariantsSection() =>
        Section("Variants", "Six ways to draw it. Primary is for the one action a view is about — mostly a form's submit.",
            Row("variants",
                Ui.Button.Key("default")["Default"],
                Ui.Button.Primary.Key("primary")["Primary"],
                Ui.Button.Filled.Key("filled")["Filled"],
                Ui.Button.Danger.Key("danger")["Danger"],
                Ui.Button.Ghost.Key("ghost")["Ghost"],
                Ui.Button.Subtle.Key("subtle")["Subtle"]));

    private static Component ColorsSection() =>
        Section("Colors", "Any Tailwind hue, as a step. It recolours every variant but danger.",
            Div.Data(Testid("ui-button-colors")).Class("flex flex-col gap-3")[
                Swatches(Ui.Color.Blue),
                Swatches(Ui.Color.Red)
            ]);

    private static Component OutlineColorsSection() =>
        Section("Outline colors", "A coloured outline keeps the white surface and mixes the hue into its label, border and hover.",
            Row("outline-colors", Hues.Select(hue => Ui.Button.Color(hue).Key(hue)[hue.ToString()])));

    private static Component SizesSection() =>
        Section("Sizes", "40px, 32px and 24px tall.",
            Row("sizes",
                Ui.Button.Key("base")["Base"],
                Ui.Button.Sm.Key("sm")["Small"],
                Ui.Button.Xs.Key("xs")["Extra small"]));

    private static Component IconsSection() =>
        Section("Icons", "An icon is a prop, so the button pads itself around it. With no label it is a square — name it.",
            Row("icons",
                Ui.Button.Icon(Ui.IconName.EllipsisHorizontal).Key("more").AriaLabel("More"),
                Ui.Button.Icon(Ui.IconName.ArrowDownTray).Key("export")["Export"],
                Ui.Button.IconTrailing(Ui.IconName.ChevronDown).Key("open")["Open"],
                Ui.Button.Subtle.Icon(Ui.IconName.XMark).Key("close").AriaLabel("Close"),
                Ui.Button.Icon(Ui.IconName.Plus).Key("add").Tooltip("Add").TooltipKbd("N")));

    private Component LoadingSection() =>
        Section("Loading", "Nothing to set. A button whose handler is still running after 200 ms shows a spinner "
            + "at the same width, tells a screen reader it is busy and drops a second press. Loading(false) "
            + "opts a stepper out, so its presses queue.",
            Row("loading",
                Ui.Button.Primary.Key("slow-save").OnClick(async () =>
                {
                    await Task.Delay(1500);
                    _saves++;
                })["Save changes"],
                Ui.Button.Icon(Ui.IconName.Plus).Key("stepper").Loading(false).OnClick(async () =>
                {
                    await Task.Delay(400);
                    _steps++;
                })["Step"],
                Span.Key("count").Data(Testid("ui-button-loading-count")).Class("text-sm text-ui-muted")[
                    $"Saved {_saves} time{(_saves == 1 ? "" : "s")} · stepped {_steps}"
                ]));

    private static Component FullWidthSection() =>
        Section("Full width", "A width class is all it takes.",
            Div.Data(Testid("ui-button-full-width")).Class("max-w-sm")[
                Ui.Button.Primary.Class("w-full")["Send invite"]
            ]);

    private static Component ButtonGroupsSection() =>
        Section("Button groups", "Related buttons fused into one control: one border between each pair, corners at the two ends.",
            Row("groups",
                Ui.ButtonGroup.Key("text")[
                    Ui.Button["Oldest"],
                    Ui.Button["Newest"],
                    Ui.Button["Top"]
                ],
                Ui.ButtonGroup.Key("icons")[
                    // Each is inside its own tooltip, which names it, and still fuses with its neighbours.
                    Ui.Button.Icon(Ui.IconName.Bars3BottomLeft).Tooltip("Align left"),
                    Ui.Button.Icon(Ui.IconName.Bars3).Tooltip("Justify"),
                    Ui.Button.Icon(Ui.IconName.Bars3BottomRight).Tooltip("Align right")
                ],
                Ui.ButtonGroup.Key("attached")[
                    Ui.Button["New product"],
                    Ui.Button.Icon(Ui.IconName.ChevronDown).AriaLabel("More options")
                ]));

    private static Component AsALinkSection() =>
        Section("As a link", "Given an Href it is an <a>. A generated route navigates inside the app without "
            + "reloading it; a plain string is a link the browser follows.",
            Row("route",
                Ui.Button.Key("google").Href("https://google.com").IconTrailing(Ui.IconName.ArrowUpRight)["Visit Google"],
                Ui.Button.Key("to-navigation")
                    .Href(PageMeta.LinkTo(Routes.UiKitNavigationPage()))["Navigation components"],
                Ui.Link.Key("to-data-display").Href(PageMeta.LinkTo(Routes.UiKitDataDisplayPage()))["Data display components"],
                Ui.Button.Ghost.Key("to-github")
                    .Href("https://github.com/pal-tamas/rask").Attributes(("target", "_blank"), ("rel", "noopener noreferrer"))["GitHub"]));

    private static Component SquareSection() =>
        Section("Square", "As wide as it is tall. Automatic for a button that shows only an icon.",
            Row("square",
                Ui.Button.Square().Key("dots")["..."],
                Ui.Button.Key("disabled").Disabled()["Disabled"]));

    private static Component InsetSection() =>
        Section("Inset", "A ghost button's padding is invisible, so it sits lower and further in than it looks. "
            + "Inset pulls it back out, and the cross lines up with the heading.",
            Div.Data(Testid("ui-button-inset")).Class("flex max-w-xs justify-between")[
                Span.Class("text-sm font-medium")["Post successfully created."],
                Ui.Button.Ghost.Sm.Icon(Ui.IconName.XMark).Inset(Ui.Inset.All).AriaLabel("Dismiss")
            ]);

    private static Component Swatches(Ui.Color hue) =>
        Div.Key(hue).Class("flex flex-wrap items-center gap-3")[
            Span.Class("w-12 text-sm font-medium text-ui-muted")[hue.ToString()],
            Ui.Button.Primary.Color(hue)["Primary"],
            Ui.Button.Filled.Color(hue)["Filled"],
            Ui.Button.Color(hue)["Outline"],
            Ui.Button.Ghost.Color(hue)["Ghost"],
            Ui.Button.Subtle.Color(hue)["Subtle"]
        ];

    private static Component Row(string name, params IEnumerable<Component?> items) =>
        Div.Data(Testid("ui-button-" + name)).Class("flex flex-wrap items-end gap-3")[items];

    private static AttrBag Testid(string value) => new("testid", value);

    private static Component Section(string heading, string blurb, Component body) =>
        Div.Key(heading).Class("mb-8")[
            H2.Class("text-lg font-semibold tracking-tight")[heading],
            P.Class("mt-1 mb-3 text-sm text-ui-muted")[blurb],
            body
        ];
}
