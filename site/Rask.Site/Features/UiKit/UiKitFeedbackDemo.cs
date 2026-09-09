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
    private string? _toast;
    private int _progress = 62;

    /// <inheritdoc />
    protected override Component? Render() =>
    [
        Section(
            "Alert",
            "Tone and fill compose, as everywhere else in the kit.",
            Div.Data(Testid("ui-alert")).Class("space-y-2")[
                UiAlert.Key("i").Message("A new version is available.").Tone(UiTone.Info),
                UiAlert.Key("s").Message("Saved.").Tone(UiTone.Success).Variant(UiVariant.Soft),
                UiAlert.Key("w").Message("Two jobs are close to their retry limit.").Tone(UiTone.Warning),
                UiAlert.Key("e").Message("Payment failed.").Tone(UiTone.Error).Variant(UiVariant.Outline)
            ]),

        Section(
            "Loading",
            "Six shapes, all saying the same thing — and none of them saying it to a screen reader. "
            + "The words beside the indicator are what gets announced.",
            Div.Data(Testid("ui-loading")).Class("flex flex-wrap items-center gap-6")[
                Spin("spinner", UiLoadingShape.Spinner, "Spinner"),
                Spin("dots", UiLoadingShape.Dots, "Dots"),
                Spin("ring", UiLoadingShape.Ring, "Ring"),
                Spin("ball", UiLoadingShape.Ball, "Ball"),
                Spin("bars", UiLoadingShape.Bars, "Bars"),
                Spin("infinity", UiLoadingShape.Infinity, "Infinity")
            ]),

        Section(
            "Progress",
            "A real <progress> element, so it reports its own value without being told to.",
            Div.Data(Testid("ui-progress")).Class("space-y-3")[
                UiProgress.Label("Upload").Value(_progress).Max(100).Tone(UiTone.Primary),
                Div.Class("flex gap-2")[
                    UiButton.Key("less").Size(UiSize.Sm)
                        .OnClick(() => { _progress = Math.Max(0, _progress - 10); })["−10"],
                    UiButton.Key("more").Size(UiSize.Sm)
                        .OnClick(() => { _progress = Math.Min(100, _progress + 10); })["+10"]
                ],
                UiRadialProgress.Label("Disk used").Percent(_progress)
            ]),

        Section(
            "Tooltip",
            "A hint and nothing more: it cannot be reached by touch and is easy to miss, so nothing "
            + "that matters should live only here. Open shows one without a hover.",
            Div.Data(Testid("ui-tooltip")).Class("flex flex-wrap items-center gap-8 pt-8")[
                UiTooltip.Key("t").Tip("Above").Placement(UiPlacement.Top)[
                    UiButton.Size(UiSize.Sm)
["Top"]                ],
                UiTooltip.Key("r").Tip("Beside").Placement(UiPlacement.Right).Tone(UiTone.Info)[
                    UiButton.Size(UiSize.Sm)
["Right"]                ],
                UiTooltip.Key("o").Tip("Always shown").Placement(UiPlacement.Top).Open(true)[
                    UiButton.Size(UiSize.Sm)
["Open"]                ]
            ]),

        Section(
            "Skeleton",
            "The shape of what is coming, so the layout does not jump when it arrives.",
            Div.Data(Testid("ui-skeleton")).Class("max-w-sm space-y-2")[
                UiSkeleton.Key("a").Class("h-4 w-3/4"),
                UiSkeleton.Key("b").Class("h-4 w-full"),
                UiSkeleton.Key("c").Class("h-24 w-full")
            ]),

        Section(
            "Toast",
            "Pinned to the viewport rather than pushed into the page's flow: an inline notice moves "
            + "everything below it the moment an action completes.",
            Div.Data(Testid("ui-toast"))[
                Div.Class("flex gap-2")[
                    UiButton.Key("ok").Tone(UiTone.Primary)
                        .OnClick(() => { _toast = "Saved."; })["Save"],
                    UiButton.Key("bad").Tone(UiTone.Error)
                        .OnClick(() => { _toast = "Payment failed."; })["Fail"]
                ],
                _toast is { } message
                    ? UiToast
                        .Message(message)
                        .Tone(message.Contains("failed", StringComparison.Ordinal)
                            ? UiTone.Error
                            : UiTone.Success)
                        .Dismiss(() => { _toast = null; })
                    : null
            ])
    ];

    private static Dictionary<string, string?> Testid(string value) => new() { ["testid"] = value };

    private static Component Spin(string key, UiLoadingShape shape, string label) =>
        UiLoading.Key(key).Text(label).Shape(shape).Size(UiSize.Lg);

    private static Component Section(string heading, string blurb, Component body) =>
        Div.Key(heading).Class("mb-8")[
            H2.Class("text-lg font-semibold tracking-tight")[heading],
            P.Class("mt-1 mb-3 text-sm text-ui-muted")[blurb],
            body
        ];
}
