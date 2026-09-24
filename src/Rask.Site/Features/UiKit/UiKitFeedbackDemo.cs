namespace Rask.Site.Features.UiKit;

/// <summary>
///     daisyUI's Feedback category, drawn with the kit.
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
        Section(
            "Alert",
            "Tone and fill compose, as everywhere else in the kit.",
            Div.Data(Testid("ui-alert")).Class("space-y-2")[
                Ui.Alert.Key("i").Tone(Ui.Tone.Info)["A new version is available."],
                Ui.Alert.Key("s").Tone(Ui.Tone.Success).Variant(Ui.Variant.Soft)["Saved."],
                Ui.Alert.Key("w").Tone(Ui.Tone.Warning)["Two jobs are close to their retry limit."],
                Ui.Alert.Key("e").Tone(Ui.Tone.Error).Variant(Ui.Variant.Outline)["Payment failed."]
            ]),

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
            ]),

        Section(
            "Progress",
            "A real <progress> element, so it reports its own value without being told to.",
            Div.Data(Testid("ui-progress")).Class("space-y-3")[
                Ui.Progress.Label("Upload").Value(_progress).Max(100).Tone(Ui.Tone.Primary),
                Div.Class("flex gap-2")[
                    Ui.Button.Key("less").Size(Ui.Size.Sm)
                        .OnClick(() => { _progress = Math.Max(0, _progress - 10); })["−10"],
                    Ui.Button.Key("more").Size(Ui.Size.Sm)
                        .OnClick(() => { _progress = Math.Min(100, _progress + 10); })["+10"]
                ],
                Ui.RadialProgress.Label("Disk used").Percent(_progress)
            ]),

        Section(
            "Tooltip",
            "A hint and nothing more: it is easy to miss, so nothing that matters should live only here. "
            + "Open shows one without a hover, Kbd teaches a shortcut where the reader is already looking, "
            + "and Toggleable shows it on a tap — a touch screen has no hover.",
            Div.Data(Testid("ui-tooltip")).Class("flex flex-wrap items-center gap-8 pt-8")[
                Ui.Tooltip.Key("k").Tip("Save").Kbd("⌘S").Position(Ui.Position.Top)[
                    Ui.Button.Size(Ui.Size.Sm)["Shortcut"]
                ],
                Ui.Tooltip.Key("tap").Tip("Tapping shows this on a phone").Toggleable(true)[
                    Ui.Icon.Name(Ui.IconName.Info).Class("size-5")
                ],
                Ui.Tooltip.Key("disabled").Tip("Available once the form is valid")[
                    Ui.Button.Size(Ui.Size.Sm).Disabled(true)["Disabled"]
                ],
                Ui.Tooltip.Key("t").Tip("Above").Position(Ui.Position.Top)[
                    Ui.Button.Size(Ui.Size.Sm)["Top"]
                ],
                Ui.Tooltip.Key("r").Tip("Beside").Position(Ui.Position.Right).Tone(Ui.Tone.Info)[
                    Ui.Button.Size(Ui.Size.Sm)["Right"]
                ],
                Ui.Tooltip.Key("o").Tip("Always shown").Position(Ui.Position.Top).Open(true)[
                    Ui.Button.Size(Ui.Size.Sm)["Open"]
                ]
            ]),

        Section(
            "Skeleton",
            "The shape of what is coming, so the layout does not jump when it arrives. Lines draws a "
            + "paragraph — the last one short, because a stack of equal bars reads as a table — and Circle is "
            + "the one an avatar leaves behind. It is aria-hidden throughout: a row of empty boxes read aloud "
            + "is worse than silence.",
            Div.Data(Testid("ui-skeleton")).Class("max-w-sm space-y-3")[
                Div.Class("flex items-center gap-3")[
                    Ui.Skeleton.Key("av").Circle(true).Class("size-10"),
                    Div.Class("grow")[Ui.Skeleton.Key("lines").Lines(2)]
                ],
                Ui.Skeleton.Key("c").Class("h-24 w-full")
            ]),

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
                    Ui.Button.Key("ok").Tone(Ui.Tone.Primary)
                        .OnClick(() => Push("Saved.", null, Ui.Tone.Success))["Save"],
                    Ui.Button.Key("undo").Variant(Ui.Variant.Outline)
                        .OnClick(() => Push("Moved to the bin.", "Order deleted", Ui.Tone.Success))["Delete"],
                    Ui.Button.Key("bad").Tone(Ui.Tone.Error)
                        .OnClick(() => Push("Payment failed.", null, Ui.Tone.Error))["Fail"]
                ],
                Ui.Toaster.Key("toaster").Position(Ui.Position.Bottom).Align(Ui.Align.End)[
                    _toasts.Select(t =>
                        Ui.Toast
                            .Key(t.Id)
                            .Message(t.Message)
                            .Heading(t.Heading)
                            .Tone(t.Tone)
                            .Duration(6.Seconds)
                            .Action(t.Heading is null
                                ? null
                                : Ui.Button.Size(Ui.Size.Xs).Variant(Ui.Variant.Ghost)
                                    .OnClick(() => Drop(t.Id))["Undo"])
                            .OnDismiss(() => Drop(t.Id)))
                ]
            ])
    ];

    private void Push(string message, string? heading, Ui.Tone tone)
    {
        _toasts.Add(new Notice(
            "t" + _nextToast++.ToString(System.Globalization.CultureInfo.InvariantCulture),
            message,
            heading,
            tone));
    }

    private void Drop(string id) => _toasts.RemoveAll(t => t.Id == id);

    private sealed record Notice(string Id, string Message, string? Heading, Ui.Tone Tone);

    private static Dictionary<string, string?> Testid(string value) => new() { ["testid"] = value };

    private static Component Spin(string key, Ui.LoadingShape shape, string label) =>
        Ui.Loading.Key(key).Text(label).Shape(shape).Size(Ui.Size.Lg);

    private static Component Section(string heading, string blurb, Component body) =>
        Div.Key(heading).Class("mb-8")[
            H2.Class("text-lg font-semibold tracking-tight")[heading],
            P.Class("mt-1 mb-3 text-sm text-ui-muted")[blurb],
            body
        ];
}
