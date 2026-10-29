namespace Rask.Site.Features.UiDocs;

/// <summary>
///     How a toast behaves: where it appears, Flux's calls in C#, how long it stays and what holds it, the runtime
///     hooks behind that, what it announces, and the sentences Rask raises one with itself.
/// </summary>
internal sealed partial class UiToastBehaviour : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiDocsBehaviour[
            Outlet(),
            Flux(),
            Stacking(),
            Timing(),
            Actions(),
            Announced(),
            Strings()
        ];

    private static UiDocsTopic Outlet() =>
        UiDocsTopic.Key("outlet").Heading("Where toasts appear").Paragraphs([
            "`Toast.Success(\"Saved\")` from any handler raises one. A toast belongs to the session, not the page, so "
            + "one raised just before navigating shows once on the page the reader lands on.",
            "`Ui.Toast` in the layout is where they appear. An app that places none gets the host's own, this one in "
            + "the bottom end corner, moved with `c.Toasts.At(Ui.ToastPosition.TopEnd).For(8.Seconds)` or `Rask:Toasts` "
            + "in appsettings; placing one replaces it.",
            "A toast sits 24px from the two viewport edges it is against, which is where Flux puts one, and start and "
            + "end follow the reading direction, so a right-to-left page mirrors. `Ui.Toast.Class(\"pt-24\")` adds to "
            + "each toast's own classes, to clear a nav bar at the top.",
        ]);

    private static Component Flux() =>
        UiDocsTopic.Key("flux").Heading("Flux's calls in C#").Paragraphs([])[
            UiDocsKeys.Column("Flux").Rows([
                ("`<flux:toast />` in the layout", "`Ui.Toast` in the layout, or nothing: the host places one."),
                ("`<flux:toast position=\"top end\" invert />`", "`Ui.Toast.TopEnd.Invert()`. `Ui.ToastPosition` is "
                    + "`BottomEnd`, `BottomCenter`, `BottomStart`, `TopEnd`, `TopCenter` or `TopStart`."),
                ("`<flux:toast class=\"pt-24\" />`", "`Ui.Toast.Class(\"pt-24\")`"),
                ("`<flux:toast.group>`", "`Ui.ToastGroup[Ui.Toast]`"),
                ("`<flux:toast.group expanded position=\"top end\">`", "`Ui.ToastGroup.Expanded().TopEnd[Ui.Toast]`"),
                ("`Flux::toast('Saved.')`, `$flux.toast('Saved.')`", "`Toast.Info(\"Saved.\")`"),
                ("`heading: 'Changes saved'`", "`Toast.Info(\"…\").Heading(\"Changes saved\")`"),
                ("`variant: 'success'`, `'warning'`, `'danger'`", "`Toast.Success(…)`, `Toast.Warning(…)`, `Toast.Error(…)`"),
                ("`duration: 1000`, `duration: 0`", "`.For(1.Second)`, `.UntilDismissed()`"),
                ("`action: ['label' => 'Undo', 'event' => …]`", "`.Action(\"Undo\", UndoChanges)`"),
                ("`action: ['label' => 'View', 'href' => …]`", "`.Action(\"View\", Routes.InvoicePage(id))`"),
                ("`link: ['label' => 'View invoice', 'href' => …]`", "`.Link(\"View invoice\", Routes.InvoicePage(id))`"),
            ])
        ];

    private static UiDocsTopic Stacking() =>
        UiDocsTopic.Key("stacking").Heading("One at a time, or a stack").Paragraphs([
            "On its own, `Ui.Toast` shows one toast at a time: a new one takes the place of the one showing, and "
            + "dismissing it takes those it replaced with it.",
            "Inside a `Ui.ToastGroup` they stack: three show at rest, the newest in front and each older one a step "
            + "back and a little narrower, and the pointer over the stack lays them all out, as `Expanded()` does for "
            + "good.",
        ]);

    private static UiDocsTopic Timing() =>
        UiDocsTopic.Key("timing").Heading("How long it stays").Paragraphs([
            "A toast goes after five seconds unless it says otherwise, by its close button, or (on its own, not in a "
            + "stack, as in Flux) by Escape. The time is counted in the browser, so it waits while the pointer is "
            + "over the toast, and only the pointer: as on Flux, a toast with focus on its close button still goes on "
            + "time. Once the time is up it fades for 350ms before it goes.",
            "The kit ships no script, so all of that is the runtime's generic hooks, written as attributes. The host "
            + "is a native `popover` the runtime shows (`data-rask-popover-open`), so a toast is in the top layer, over "
            + "an open dialog. `data-rask-dismiss-after=\"<ms>\"` has the runtime press the toast's own "
            + "`[data-rask-dismiss]` button when the time is up, `data-rask-dismiss-hold=\"pointer\"` says only the "
            + "pointer holds it, and `data-rask-escape` is Escape — heard wherever focus is, and taken from nothing: "
            + "as on Flux, one Escape closes both a toast and the modal under it.",
            "A `Ui.ToastGroup` is one `data-rask-dismiss-scope`: the pointer anywhere over the stack holds every "
            + "toast in it, and when it leaves each runs on from where it stopped rather than starting again.",
            "Pressing the button rather than hiding the element is the point: the outlet takes the toast off its "
            + "list, so the next render agrees with the screen. That timed press is sent as the runtime's own, so it "
            + "carries none of the reader's unsent typing ahead of it.",
            "The host is always on the page, closed and empty while there is nothing to show, as Flux's is. A toast "
            + "arriving, leaving or taking another's place is a change inside it, which the live diff ships as one "
            + "insert or removal.",
        ]);

    private static UiDocsTopic Actions() =>
        UiDocsTopic.Key("actions").Heading("Actions and links").Paragraphs([
            "An action given a route stays a real link: a new tab, a copied address. An action given a handler is a "
            + "button that shows a spinner while the handler runs (the runtime's `data-loading`), then takes the "
            + "toast down.",
        ]);

    private static UiDocsTopic Announced() =>
        UiDocsTopic.Key("announced").Heading("What it announces").Paragraphs([
            "Like Flux's, the host is `role=\"status\"` and each toast `aria-atomic=\"true\"`, so a toast is announced "
            + "politely, rather than interrupting, and whole. A spinner tells a screen reader nothing and a colour is "
            + "invisible to a reader who cannot tell it apart, so the variant is carried by its icon's shape as well "
            + "as its colour: a check, a triangle, a circle with a mark.",
        ]);

    private static Component Strings() =>
        UiDocsTopic.Key("strings").Heading("The toasts Rask raises itself").Paragraphs([
            "A handler that throws, and a form save that fails, raise an error toast with a sentence of Rask's own, "
            + "followed by the reference the failure is logged under. Each is a `RaskString`, in English and in "
            + "Hungarian out of the box, and replaced like any other through localization.",
        ])[
            UiDocsKeys.Column("String").Rows([
                ("`HandlerFailed`", "That did not work. Please try again. — Nem sikerült. Kérjük, próbálja újra."),
                ("`FormSaveFailed`", "The save failed. Please try again. — A mentés nem sikerült. Kérjük, próbálja újra."),
                ("`ErrorReference`", "The problem has been recorded. If it keeps happening, quote this reference: {0} — "
                                     + "A hibát rögzítettük. Ha újra előfordul, erre az azonosítóra hivatkozzon: {0}"),
            ])
        ];
}
