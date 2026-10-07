namespace Rask.Site.Features.UiKit;

/// <summary>
///     The kit's feedback components: what a page says while, and after, something happens.
/// </summary>
/// <remarks>
///     The recurring theme here is what gets ANNOUNCED. A spinner tells a screen reader nothing, a
///     colour is invisible to a reader who cannot distinguish it, and a tooltip cannot be reached by
///     touch at all — so every component in this category carries its meaning in words as well.
/// </remarks>
public sealed partial class UiKitFeedbackDemo : Component
{
    // Which of Flux's layouts the toasts below are raised into. A toast is raised from anywhere and shown by
    // the Ui.Toast the layout places; this demo places its own, so it can show each of them.
    private ToastLayout _layout;
    private int _progress = 62;

    /// <inheritdoc />
    protected override Component? Render() =>
    [
        CalloutSection(),
        LoadingSection(),
        ProgressSection(),
        TooltipSection(),
        SkeletonSection(),
        ToastSection()
    ];

    private static Component CalloutSection() =>
        Section(
            "Callout",
            "Flux UI's callout, example for example: an icon, a heading, text, and what to do about it. It "
            + "announces nothing by itself — one that appears because something happened says so with Role.",
            UiKitCalloutDemo.Key("callouts"));

    private static Component LoadingSection() =>
        Section(
            "Loading",
            "Six shapes, all saying the same thing — and none of them saying it to a screen reader. "
            + "The words beside the indicator are what gets announced.",
            Div.Data(Testid("ui-loading")).Class("flex flex-wrap items-center gap-6")[
                Spin("spinner", Ui.LoadingShape.Spinner, "Spinner"),
                Spin("dots", Ui.LoadingShape.Dots, "Dots"),
                Spin("ring", Ui.LoadingShape.Ring, "Ring"),
                Spin("ball", Ui.LoadingShape.Ball, "Ball"),
                Spin("bars", Ui.LoadingShape.Bars, "Bars"),
                Spin("infinity", Ui.LoadingShape.Infinity, "Infinity")
            ]);

    private Component ProgressSection() =>
        Section(
            "Progress",
            "Flux UI's progress bar: a track and the bar filling it, announced as a progressbar with its "
            + "value and maximum. Max sets a scale other than a hundred, a colour replaces the accent, a "
            + "class makes it taller — and the bar moves to a new value rather than jumping.",
            Div.Data(Testid("ui-progress")).Class("max-w-64 space-y-6")[
                Ui.Progress.Key("intro").Value(75).Aria("label", "Sync"),
                Ui.Progress.Key("max").Value(3).Max(7).Aria("label", "Steps done"),
                Ui.Progress.Key("color").Value(75).Color(Ui.Color.Purple).Aria("label", "Quota"),
                Ui.Progress.Key("height").Value(75).Class("h-3").Aria("label", "Battery"),
                Div.Key("label")[
                    Div.Id("fb-upload").Class("mb-3 text-sm font-medium")["Upload progress"],
                    Ui.Progress.Value(42).Color(Ui.Color.Blue).Aria("labelledby", "fb-upload"),
                    P.Class("mt-3 text-sm text-ui-muted")["Uploading 3 of 7 files..."]
                ],
                Div.Key("value")[
                    Div.Id("fb-storage").Class("mb-3 flex text-sm font-medium")[
                        "Storage",
                        Span.Class("ms-auto tabular-nums")[$"{_progress}%"]
                    ],
                    Ui.Progress.Data(Testid("ui-progress-storage")).Value(_progress).Aria("labelledby", "fb-storage")
                ],
                Div.Key("controls").Class("flex gap-2")[
                    Ui.Button.Key("less").Sm
                        .OnClick(() => { _progress = Math.Max(0, _progress - 10); })["−10"],
                    Ui.Button.Sm.Key("more")
                        .OnClick(() => { _progress = Math.Min(100, _progress + 10); })["+10"]
                ]
            ]);

    private static Component TooltipSection() =>
        Section(
            "Tooltip",
            "Flux's tooltip: a line of help beside whatever it wraps, shown on hover and on keyboard focus, and "
            + "said by a screen reader because the trigger is wired to it. It is a hint — nothing that matters "
            + "should live only here — and what must reach a phone is Toggleable, opened by a tap.",
            Div.Data(Testid("ui-tooltip")).Class("flex flex-wrap items-center gap-8 pt-8")[
                Ui.Tooltip.Key("settings").Content("Settings")[SettingsButton()],
                Div.Data(Testid("ui-tooltip-info")).Class("flex items-center gap-2 text-sm font-medium")[
                    "Tax identification number",
                    Ui.Tooltip.Key("info").Toggleable()[
                        Ui.Button.Sm.Ghost.Icon(Ui.IconName.InformationCircle),
                        Ui.TooltipContent.Class("max-w-[20rem] space-y-2")[
                            P["For US businesses, enter your 9-digit Employer Identification Number (EIN) without hyphens."],
                            P["For European companies, enter your VAT number including the country prefix (e.g., DE123456789)."]
                        ]
                    ]
                ],
                Div.Data(Testid("ui-tooltip-positions")).Class("flex gap-8")[
                    Ui.Tooltip.Key("top").Content("Settings").Top[SettingsButton()],
                    Ui.Tooltip.Key("right").Content("Settings").Right[SettingsButton()],
                    Ui.Tooltip.Key("bottom").Content("Settings").Bottom[SettingsButton()],
                    Ui.Tooltip.Key("left").Content("Settings").Left[SettingsButton()]
                ],
                Ui.Tooltip.Key("kbd").Content("Save").Kbd("⌘S")[Ui.Button.Sm["Shortcut"]],
                Ui.Tooltip.Key("start").Content("Flush with its start edge").Bottom.Start[Ui.Button.Sm["Aligned"]],
                Ui.Tooltip.Key("disabled").Content("Available once the form is valid")[
                    Ui.Button.Sm.Disabled()["Disabled"]
                ]
            ]);

    // Named by its tooltip: an icon button has no text of its own, so the tooltip becomes its label.
    private static UiButton SettingsButton() =>
        Ui.Button.Icon(Ui.IconName.Cog6Tooth).IconVariant(Ui.IconVariant.Outline);

    private static Component SkeletonSection() =>
        Section(
            "Skeleton",
            "Flux UI's skeleton: the shape of what is coming, so the layout does not jump when it arrives. A "
            + "skeleton is a block the call site sizes and rounds, a line keeps the height of the text it "
            + "stands in for, and a group animates everything inside it — a shimmer or a pulse — however deep.",
            Div.Data(Testid("ui-skeleton")).Class("max-w-lg space-y-8")[
                Ui.SkeletonGroup.Key("intro").Shimmer.Class("flex max-w-64 items-center gap-4")[
                    Ui.Skeleton.Class("size-10 rounded-full"),
                    Div.Class("flex-1")[
                        Ui.SkeletonLine,
                        Ui.SkeletonLine.Class("w-1/2")
                    ]
                ],
                Ui.SkeletonGroup.Key("lines").Shimmer.Class("max-w-sm")[
                    Ui.SkeletonLine.Class("mb-2 w-1/4"),
                    Ui.SkeletonLine,
                    Ui.SkeletonLine,
                    Ui.SkeletonLine.Class("w-3/4")
                ],
                Div.Key("animation").Class("flex max-w-sm flex-col gap-6")[
                    Div[Div.Class("mb-2 text-sm font-medium")["None"], Ui.Skeleton],
                    Div[Div.Class("mb-2 text-sm font-medium")["Shimmer"], Ui.Skeleton.Shimmer],
                    Div[Div.Class("mb-2 text-sm font-medium")["Pulse"], Ui.Skeleton.Pulse]
                ],
                Ui.SkeletonGroup.Key("table").Shimmer[
                    Ui.Table.Class("mb-0")[
                        Thead[Tr[Th["Customer"], Th["Date"], Th["Status"], Th["Amount"]]],
                        Tbody[Enumerable.Range(1, 5).Select(OrderRow)]
                    ]
                ],
                Ui.Card.Key("chart")[
                    Div.Class("flex flex-col gap-6")[
                        Div.Class("flex gap-12")[
                            Div[
                                Ui.Text["Today"],
                                Ui.Heading.Xl.Class("mt-2 tabular-nums")["$---"],
                                Ui.Text.Class("mt-2 tabular-nums")["-:-- PM"]
                            ],
                            Div[
                                Ui.Text["Yesterday"],
                                Ui.Heading.Lg.Class("mt-2 tabular-nums")["$---"]
                            ]
                        ],
                        Ui.Skeleton.Shimmer.Class("aspect-[4/1] size-full rounded-lg")
                    ]
                ]
            ]);

    // The first line of each row a different width, as a column of real names would be.
    private static Component OrderRow(int order) =>
        Tr.Key(order)[
            Td[
                Div.Class("flex items-center gap-2")[
                    Ui.Skeleton.Class("size-5 rounded-full"),
                    Div.Class("flex-1")[
                        Ui.SkeletonLine.Style(
                            string.Create(System.Globalization.CultureInfo.InvariantCulture, $"width:{50 + ((order * 37) % 51)}%"))
                    ]
                ]
            ],
            Td[Ui.SkeletonLine],
            Td[Ui.SkeletonLine],
            Td[Ui.SkeletonLine]
        ];

    private Component ToastSection() =>
        Section(
            "Toast",
            "Flux's toast. It is RAISED, not placed: Toast.Success(\"Saved\") from any handler, with .Heading, "
            + ".Action, .Link, .For and .UntilDismissed for what Flux::toast() takes. Ui.Toast in the layout is "
            + "where they appear — one at a time, a new one taking the place of the one showing — and "
            + "Ui.ToastGroup around it stacks them into a deck that opens under the pointer. A toast goes after "
            + "five seconds, counted in the browser so it waits while the pointer is over it, or by its close "
            + "button.",
            Div.Data(Testid("ui-toast")).Class("space-y-3")[
                Div.Class("flex flex-wrap gap-2")[
                    Ui.Button.Key("plain").Primary.Id("toast-plain")
                        .OnClick(() => Toast.Info("Your changes have been saved."))["Save changes"],
                    Ui.Button.Key("heading").Id("toast-heading")
                        .OnClick(() => Toast.Info("You can always update this in your settings.").Heading("Changes saved"))["With heading"],
                    Ui.Button.Key("success").Id("toast-success")
                        .OnClick(() => Toast.Success("The post has been created successfully.").Heading("Post created"))["Success"],
                    Ui.Button.Key("warning").Id("toast-warning")
                        .OnClick(() => Toast.Warning("Your post has unsaved changes.").Heading("Unsaved changes"))["Warning"],
                    Ui.Button.Key("danger").Id("toast-danger")
                        .OnClick(() => Toast.Error("Your changes have not been saved.").Heading("Something went wrong"))["Danger"]
                ],
                Div.Class("flex flex-wrap gap-2")[
                    Ui.Button.Key("action").Id("toast-action")
                        .OnClick(() => Toast.Success("Your updates are now live.").Heading("Changes saved")
                            .Action("Undo", () => Toast.Info("Changes undone.")))["With an action"],
                    Ui.Button.Key("link").Id("toast-link")
                        .OnClick(() => Toast.Success("Invoice created.")
                            .Link("View invoice", PageMeta.LinkTo(Routes.UiKitDataDisplayPage())))["With a link"],
                    Ui.Button.Key("brief").Id("toast-brief")
                        .OnClick(() => Toast.Info("Your changes have been saved.").For(1.Second))["One second"],
                    Ui.Button.Key("permanent").Id("toast-permanent")
                        .OnClick(() => Toast.Info("Your changes have been saved.").UntilDismissed())["Permanent"]
                ],
                Div.Class("flex flex-wrap items-center gap-2")[
                    Span.Class("text-sm text-ui-muted")["Shown in:"],
                    Layouts.Select(Choice)
                ],
                Placed()
            ]);

    private static readonly (ToastLayout Layout, string Label)[] Layouts =
    [
        (ToastLayout.Default, "Ui.Toast"),
        (ToastLayout.Inverted, "Ui.Toast.Invert()"),
        (ToastLayout.TopEnd, "Ui.Toast.TopEnd"),
        (ToastLayout.Stack, "Ui.ToastGroup[Ui.Toast]"),
        (ToastLayout.Expanded, "Ui.ToastGroup.Expanded()[Ui.Toast]"),
    ];

    private Component Choice((ToastLayout Layout, string Label) choice)
    {
        var button = Ui.Button.Key(choice.Label).Sm.Id("toast-layout-" + choice.Layout.ToString().ToLowerInvariant())
            .OnClick(() => _layout = choice.Layout);
        return (choice.Layout == _layout ? button.Primary : button.Outline)[choice.Label];
    }

    // What an app writes once, in its layout.
    private Component Placed() => _layout switch
    {
        ToastLayout.Inverted => Ui.Toast.Invert(),
        ToastLayout.TopEnd => Ui.Toast.TopEnd,
        ToastLayout.Stack => Ui.ToastGroup[Ui.Toast],
        ToastLayout.Expanded => Ui.ToastGroup.Expanded()[Ui.Toast],
        _ => Ui.Toast,
    };

    private enum ToastLayout
    {
        Default,
        Inverted,
        TopEnd,
        Stack,
        Expanded,
    }

    private static AttrBag Testid(string value) => new("testid", value);

    private static UiLoading Spin(string key, Ui.LoadingShape shape, string label) =>
        Ui.Loading.Key(key).Text(label).Shape(shape).Lg;

    private static Component Section(string heading, string blurb, Component body) =>
        Div.Key(heading).Class("mb-8")[
            H2.Class("text-lg font-semibold tracking-tight")[heading],
            P.Class("mt-1 mb-3 text-sm text-ui-muted")[blurb],
            body
        ];
}
