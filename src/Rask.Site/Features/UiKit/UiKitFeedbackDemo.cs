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
    // The page owns the list, which is the whole contract: a toast leaves by the page removing it, whether
    // the reader pressed Dismiss or the runtime pressed it for them after the Duration ran out.
    private readonly List<Notice> _toasts = [];
    private int _nextToast;
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
                    Ui.Button.Key("more").Sm
                        .OnClick(() => { _progress = Math.Min(100, _progress + 10); })["+10"]
                ]
            ]);

    private static Component TooltipSection() =>
        Section(
            "Tooltip",
            "A hint and nothing more: it is easy to miss, so nothing that matters should live only here. "
            + "Open shows one without a hover, Kbd teaches a shortcut where the reader is already looking, "
            + "and Toggleable shows it on a tap — a touch screen has no hover.",
            Div.Data(Testid("ui-tooltip")).Class("flex flex-wrap items-center gap-8 pt-8")[
                Ui.Tooltip.Key("k").Tip("Save").Kbd("⌘S").Position(Ui.Position.Top)[
                    Ui.Button.Sm["Shortcut"]
                ],
                Ui.Tooltip.Key("tap").Tip("Tapping shows this on a phone").Toggleable()[
                    Ui.Icon.Name(Ui.IconName.InformationCircle).Class("size-5")
                ],
                Ui.Tooltip.Key("disabled").Tip("Available once the form is valid")[
                    Ui.Button.Sm.Disabled()["Disabled"]
                ],
                Ui.Tooltip.Key("t").Tip("Above").Position(Ui.Position.Top)[
                    Ui.Button.Sm["Top"]
                ],
                Ui.Tooltip.Key("r").Tip("Beside").Position(Ui.Position.Right).Info[
                    Ui.Button.Sm["Right"]
                ],
                Ui.Tooltip.Key("o").Tip("Always shown").Position(Ui.Position.Top).Open()[
                    Ui.Button.Sm["Open"]
                ]
            ]);

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
            "Pinned to the viewport rather than pushed into the page's flow: an inline notice moves "
            + "everything below it the moment an action completes. A Ui.Toaster stacks several in a corner, "
            + "and the PAGE owns the list — which is why Duration dismisses by clicking the toast's own "
            + "button rather than hiding the element: the page's handler runs, so the page takes the toast "
            + "off its list and the next render agrees with the screen. The countdown pauses while the "
            + "pointer is over it or focus is inside it, so reaching for Undo does not lose it. A failure "
            + "says role=alert; everything else is announced politely.",
            Div.Data(Testid("ui-toast"))[
                Div.Class("flex flex-wrap gap-2")[
                    Ui.Button.Key("ok").Primary
                        .OnClick(() => Push("Saved.", null, Ui.Tone.Success))["Save"],
                    Ui.Button.Key("undo").Outline
                        .OnClick(() => Push("Moved to the bin.", "Order deleted", Ui.Tone.Success))["Delete"],
                    Ui.Button.Key("bad").Error
                        .OnClick(() => Push("Payment failed.", null, Ui.Tone.Error))["Fail"]
                ],
                Ui.Toaster.Key("toaster").Position(Ui.Position.Bottom).Align(Ui.Align.End)[
                    _toasts.Select(t =>
                        Ui.Toast
                            .Key(t.Id)
                            .Message(t.Message)
                            .Title(t.Heading)
                            .Tone(t.Tone)
                            .Duration(6.Seconds)
                            .Action(t.Heading is null
                                ? null
                                : Ui.Button.Xs.Ghost
                                    .OnClick(() => Drop(t.Id))["Undo"])
                            .OnDismiss(() => Drop(t.Id)))
                ]
            ]);

    private void Push(string message, string? heading, Ui.Tone tone)
    {
        _toasts.Add(new Notice(
            "t" + _nextToast++.ToString(System.Globalization.CultureInfo.InvariantCulture),
            message,
            heading,
            tone));
    }

    private void Drop(string id) => _toasts.RemoveAll(t => string.Equals(t.Id, id, StringComparison.Ordinal));

    private sealed record Notice(string Id, string Message, string? Heading, Ui.Tone Tone);

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
