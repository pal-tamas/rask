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
    // Which of Flux's layouts the toasts below are raised into. A toast is raised from anywhere and shown by
    // the Ui.Toast the layout places; this demo places its own, so it can show each of them.
    private ToastLayout _layout;
    private int _progress = 62;

    /// <inheritdoc />
    protected override Component? Render() =>
    [
        AlertSection(),
        LoadingSection(),
        ProgressSection(),
        TooltipSection(),
        SkeletonSection(),
        ToastSection()
    ];

    private static Component AlertSection() =>
        Section(
            "Alert",
            "Tone and fill compose, as everywhere else in the kit.",
            Div.Data(Testid("ui-alert")).Class("space-y-2")[
                Ui.Alert.Key("i").Info["A new version is available."],
                Ui.Alert.Key("s").Success.Soft["Saved."],
                Ui.Alert.Key("w").Warning["Two jobs are close to their retry limit."],
                Ui.Alert.Key("e").Error.Outline["Payment failed."]
            ]);

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
            "A real <progress> element, so it reports its own value without being told to.",
            Div.Data(Testid("ui-progress")).Class("space-y-3")[
                Ui.Progress.Label("Upload").Value(_progress).Max(100).Primary,
                Div.Class("flex gap-2")[
                    Ui.Button.Key("less").Sm
                        .OnClick(() => { _progress = Math.Max(0, _progress - 10); })["−10"],
                    Ui.Button.Key("more").Sm
                        .OnClick(() => { _progress = Math.Min(100, _progress + 10); })["+10"]
                ],
                Ui.RadialProgress.Label("Disk used").Percent(_progress)
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
            "The shape of what is coming, so the layout does not jump when it arrives. Lines draws a "
            + "paragraph — the last one short, because a stack of equal bars reads as a table — and Circle is "
            + "the one an avatar leaves behind. It is aria-hidden throughout: a row of empty boxes read aloud "
            + "is worse than silence.",
            Div.Data(Testid("ui-skeleton")).Class("max-w-sm space-y-3")[
                Div.Class("flex items-center gap-3")[
                    Ui.Skeleton.Key("av").Circle().Class("size-10"),
                    Div.Class("grow")[Ui.Skeleton.Key("lines").Lines(2)]
                ],
                Ui.Skeleton.Key("c").Class("h-24 w-full")
            ]);

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
