using System.Globalization;
using Rask.Core.Messaging;

namespace Rask;

public sealed partial class UiToast
{
    // How long a timed toast takes to fade once its time is up — ui.css runs that fade at the end of the
    // countdown, and the runtime presses the close button when it is over.
    private const int LeaveMilliseconds = 350;

    private const string Card =
        "flex rounded-xl border border-zinc-200 border-b-zinc-300/80 bg-white p-2 shadow-lg "
        + "dark:border-zinc-600 dark:bg-zinc-700 "
        + "group-data-invert/toast:border-zinc-700 group-data-invert/toast:bg-zinc-800 "
        + "dark:group-data-invert/toast:border-zinc-200 dark:group-data-invert/toast:border-b-zinc-300/80 "
        + "dark:group-data-invert/toast:bg-white";

    // The first line: the heading, or the text when there is none.
    private const string Lead =
        "text-sm font-medium text-zinc-800 dark:text-white "
        + "group-data-invert/toast:text-white dark:group-data-invert/toast:text-zinc-800";

    private const string Detail =
        "text-sm text-zinc-500 dark:text-zinc-300 "
        + "group-data-invert/toast:text-zinc-300 dark:group-data-invert/toast:text-zinc-500";

    private const string LinkClass =
        "mt-2 block text-sm font-medium text-zinc-800 underline underline-offset-[6px] dark:text-white "
        + "group-data-invert/toast:text-white dark:group-data-invert/toast:text-zinc-800";

    private const string ActionClass =
        "flex h-8 items-center justify-center gap-2 rounded-md bg-zinc-800/5 px-3 text-sm "
        + "font-medium whitespace-nowrap text-zinc-800 hover:bg-zinc-800/10 "
        + "dark:bg-white/10 dark:text-white dark:hover:bg-white/20 "
        + "group-data-invert/toast:bg-white/10 group-data-invert/toast:text-white group-data-invert/toast:hover:bg-white/20 "
        + "dark:group-data-invert/toast:bg-zinc-800/5 dark:group-data-invert/toast:text-zinc-800 "
        + "dark:group-data-invert/toast:hover:bg-zinc-800/10";

    private const string CloseClass =
        "flex size-8 items-center justify-center gap-2 truncate rounded-md text-sm font-medium text-zinc-400 "
        + "hover:bg-zinc-800/5 hover:text-zinc-800 dark:hover:bg-white/15 dark:hover:text-white "
        + "group-data-invert/toast:hover:bg-white/15 group-data-invert/toast:hover:text-white "
        + "dark:group-data-invert/toast:hover:bg-zinc-800/5 dark:group-data-invert/toast:hover:text-zinc-800";

    /// <summary>One toast.</summary>
    /// <param name="message">What it says.</param>
    /// <param name="close">Takes it down.</param>
    /// <param name="look">The layout's side of how it is drawn.</param>
    /// <param name="ahead">In a stack, the id of the toast in front of this one; <c>null</c> for the front one.</param>
    /// <param name="behind">In a stack, how many toasts are in front of this one.</param>
    private static Rask.Core.Component Dialog(ToastMessage message, Action close, UiToastLook look, int? ahead, int behind)
    {
        var marks = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["ui-toast-dialog"] = null,
            ["variant"] = Variant(message.Level),
        };
        if (look.Invert)
        {
            marks["invert"] = null;
        }

        var style = look.Stack is null ? null : Place(message.Id, ahead, behind);
        if (Shows(message, look.Duration) is { } milliseconds)
        {
            marks["rask-dismiss-after"] = (milliseconds + LeaveMilliseconds).ToString(CultureInfo.InvariantCulture);
            style += string.Create(CultureInfo.InvariantCulture, $"--ui-toast-duration:{milliseconds}ms");
        }

        return Div.Key(message.Id)
            .Class("group/toast max-w-sm", look.Stack is null ? null : "absolute w-xs sm:w-sm", look.Class)
            .Style(style).Data(marks).Aria("atomic", "true")[
                Div.Class(Card)[
                    Div.Class(message.Action is null ? "flex flex-1 items-start gap-4 overflow-hidden" : "flex flex-1 items-start gap-2 overflow-hidden")[
                        Div.Class("flex flex-1 gap-2 py-1.5 ps-2.5")[
                            Glyph(message.Level),
                            Div[Words(message)]
                        ],
                        Div.Class("flex items-center gap-1")[
                            message.Action is { } action ? ActionControl(action, close) : null,
                            CloseControl(close, look.Stack is null)
                        ]
                    ]
                ]
            ];
    }

    // Where a stacked toast is in its stack, for ui.css: how far back, its own anchor, and the one in front.
    private static string Place(int id, int? ahead, int behind) =>
        ahead is { } front
            ? string.Create(CultureInfo.InvariantCulture, $"--ui-toast-index:{behind};anchor-name:--ui-toast-{id};position-anchor:--ui-toast-{front};")
            : string.Create(CultureInfo.InvariantCulture, $"--ui-toast-index:0;anchor-name:--ui-toast-front,--ui-toast-{id};");

    // The toast's own .For(…), else the layout's; none for .UntilDismissed().
    private static long? Shows(ToastMessage message, TimeSpan fallback)
    {
        var duration = message.Duration ?? fallback;
        return duration > TimeSpan.Zero && duration != Timeout.InfiniteTimeSpan ? (long)duration.TotalMilliseconds : null;
    }

    private static string Variant(ToastLevel level) => level switch
    {
        ToastLevel.Success => "success",
        ToastLevel.Warning => "warning",
        ToastLevel.Error => "danger",
        _ => "",
    };

    // Flux writes these three straight into the toast, so they carry nothing flux:icon would add.
    private static Rask.Core.Component? Glyph(ToastLevel level) => level switch
    {
        ToastLevel.Success => UiIcon.Bare(Ui.IconName.CheckCircle, Ui.IconVariant.Micro, "mt-0.5 size-4 shrink-0 text-lime-600 dark:text-lime-400"),
        ToastLevel.Warning => UiIcon.Bare(Ui.IconName.ExclamationTriangle, Ui.IconVariant.Micro, "mt-0.5 size-4 shrink-0 text-amber-500 dark:text-amber-400"),
        ToastLevel.Error => UiIcon.Bare(Ui.IconName.ExclamationCircle, Ui.IconVariant.Micro, "mt-0.5 size-4 shrink-0 text-rose-500 dark:text-rose-400"),
        _ => null,
    };

    private static Rask.Core.Component?[] Words(ToastMessage message) =>
    [
        message.Title is { Length: > 0 } heading ? Div.Class("pb-2", Lead)[heading] : null,
        Div.Class(message.Title is { Length: > 0 } ? Detail : Lead)[message.Message],
        message.Link is { } link ? A.Href(link.Href).Class(LinkClass)[link.Label] : null
    ];

    // A link action stays a real link — a new tab, a copied address. A button runs its handler, shows the
    // runtime's own in-flight mark while it does, then takes the toast down.
    private static Rask.Core.Component ActionControl(ToastAction action, Action close) =>
        Div.Class("flex items-center").Attributes(("data-ui-toast-action", null))[
            action.Href is { } href
                ? A.Href(href).Class(ActionClass).Attributes(("data-ui-toast-action-link", null))[
                    Span.Attributes(("data-ui-toast-action-label", null))[action.Label]
                ]
                : Button.Type(ButtonType.Button).Class("group/action relative", ActionClass).Attributes(("data-ui-toast-action-button", null))
                    .OnClick(async () =>
                    {
                        await action.Run.Invoke();
                        close();
                    })[
                        Span.Class("transition-opacity group-data-loading/action:opacity-0")
                            .Attributes(("data-ui-toast-action-label", null))[action.Label],
                        Span.Class("absolute inset-0 flex items-center justify-center opacity-0 transition-opacity group-data-loading/action:opacity-100")
                            .Attributes(("data-ui-loading-indicator", null))[
                                Spinner()
                            ]
                    ]
        ];

    // The spinner Flux's toast draws while its action works: a faint ring and a quarter of it, darker.
    private static Rask.Core.Component Spinner() =>
        Svg.Class("size-4 animate-spin").Aria("hidden", "true").Fill("none").ViewBox("0 0 24 24")[
            Circle.Class("opacity-25").Stroke("currentColor").StrokeWidth("4").Cx("12").Cy("12").R("10"),
            SvgPath.Class("opacity-75").Fill("currentColor").D("M4 12a8 8 0 0 1 8-8V0C5.373 0 0 5.373 0 12h4Z")
        ];

    // data-rask-dismiss is what the runtime presses when the countdown ends. Escape presses it too, for a
    // toast on its own (data-rask-shortcut) — a stack is not taken down by a key, as Flux's is not.
    private static Rask.Core.Component CloseControl(Action close, bool escapes)
    {
        var marks = new Dictionary<string, string?>(StringComparer.Ordinal) { ["data-rask-dismiss"] = null };
        if (escapes)
        {
            marks["data-rask-shortcut"] = "escape";
        }

        return Div.Class("flex items-center")[
            Button.Type(ButtonType.Button).Class(CloseClass).Attributes(marks).OnClick(close)[
                Div[UiIcon.Slot(Ui.IconName.XMark, Ui.IconVariant.Mini, "size-5")]
            ]
        ];
    }
}
