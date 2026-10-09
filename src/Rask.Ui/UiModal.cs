using System.Globalization;

namespace Rask;

/// <summary>
///     Flux's <c>flux:modal</c>: content in a layer above the page — a centred panel, or with
///     <see cref="Flyout" /> a sheet anchored to an edge.
/// </summary>
/// <remarks>
///     <para>
///     A real modal <c>&lt;dialog&gt;</c>: the top layer, the page behind made inert, focus kept inside it and
///     handed back to what opened it. Give it a <see cref="Name" /> and a <see cref="UiModalTrigger" /> naming
///     it, and the browser opens it: the trigger is an invoker (<c>command="show-modal"</c>), so no handler
///     runs and nothing round-trips.
///     </para>
///     <para>
///     Set <see cref="Open" /> and the page owns the state instead, which is Rask's <c>wire:model</c>: the
///     dialog says <c>data-rask-modal-open</c> and the runtime shows and closes it to match — the same modal,
///     the same backdrop. <see cref="OnClose" /> is where the page learns that the reader closed it.
///     </para>
///     <para>
///     What Flux does in script the dialog asks the runtime for: <c>data-rask-modal</c> says which of Escape
///     and a press outside dismiss it, and <c>data-rask-lock="scroll"</c> holds the page still behind it.
///     </para>
/// </remarks>
public sealed partial class UiModal : Component
{
    private static readonly UiPartMarker Marker = new("ui-modal");

    private static readonly UiPartMarker CloseMarker = new("ui-modal-close");

    // What every variant shares: fading and settling in over 150ms, leaving in 75, and the page dimmed behind.
    private const string Motion =
        "opacity-0 transition-all transition-discrete duration-75 ease-[ease] open:opacity-100 open:duration-150 "
        + "starting:open:opacity-0 open:[transform:translate(0)_scale(1)] backdrop:bg-[rgba(0,0,0,0.1)] backdrop:opacity-0 "
        + "backdrop:transition-all backdrop:transition-discrete backdrop:duration-75 backdrop:ease-[ease] "
        + "open:backdrop:opacity-100 open:backdrop:duration-150 starting:open:backdrop:opacity-0 "
        + "open:backdrop:[transform:translate(0)_scale(1)]";

    private const string Grows = "[transform:scale(0.95)] starting:open:[transform:scale(0.95)]";

    private const string Panel = "border-0 bg-white text-inherit shadow-lg ring ring-black/5 dark:bg-zinc-800 dark:ring-zinc-700";

    // The two sizes are defaults a call site's own `min-w-*` / `max-w-*` replaces, as they are in Flux.
    private const string Box = "m-auto rounded-xl p-6 [:where(&)]:min-w-xs [:where(&)]:max-w-xl";

    // A sheet against an edge, with a line on its page side that only dark mode draws.
    private const string Sheet = "fixed overflow-y-auto border-transparent bg-white p-8 text-inherit dark:border-zinc-700 dark:bg-zinc-800";

    private const string Floating = "fixed overflow-y-auto rounded-xl p-8";

    private const string Bare = "m-auto border-0 bg-transparent p-0 text-inherit";

    private readonly int _instance = UiInstanceCounter.Next();

    /// <summary>What a <see cref="UiModalTrigger" /> opens it by. Unique on the page: it is the dialog's id.</summary>
    public string? Name { get; set; }

    /// <summary>Anchors it to an edge of the viewport, full height, for longer forms.</summary>
    public bool? Flyout { get; set; }

    /// <summary>How it is drawn: the panel, a floating flyout, or nothing at all around the content.</summary>
    public Ui.ModalVariant? Variant { get; set; }

    /// <summary>The edge a flyout opens from. Right when unset.</summary>
    public Ui.ModalPosition? Position { get; set; }

    /// <summary>
    ///     What scrolls when the content is taller than the viewport: the dialog itself, or with
    ///     <see cref="Ui.ModalScroll.Body" /> the whole layer, so the panel runs off the bottom of the screen.
    /// </summary>
    public Ui.ModalScroll? Scroll { get; set; }

    /// <summary>Whether a click outside closes it. On unless this is <see langword="false" />.</summary>
    public bool? Dismissible { get; set; }

    /// <summary>Whether Escape closes it. On unless this is <see langword="false" />.</summary>
    public bool? Escapable { get; set; }

    /// <summary>Whether it shows the close button in its corner. On unless this is <see langword="false" />.</summary>
    public bool? Closable { get; set; }

    /// <summary>
    ///     Hands the open state to the page, in place of Flux's <c>wire:model</c>. Unset, a named modal is
    ///     opened by its triggers and the browser keeps the state.
    /// </summary>
    public bool? Open { get; set; }

    /// <summary>Runs when it closes, by any means. On the state-driven path this is where the page stops rendering it open.</summary>
    public Callback OnClose { get; set; }

    /// <summary>Runs when it is dismissed — a click outside or Escape — before <see cref="OnClose" />.</summary>
    public Callback OnCancel { get; set; }

    /// <summary>Classes for the panel, added to its own: <c>md:w-96</c> sets the width.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        // The dialog's id: what its own buttons, and a trigger, name in their commands.
        var id = Name ?? "ui-modal-" + _instance.ToString(CultureInfo.InvariantCulture);
        var dialog = Dialog.Id(id).Class(UiClass.Compose(Motion, Shape(), ScrollsBody ? null : Class)).Data(Marks(id));

        if (OnClose.HasValue)
        {
            dialog = dialog.OnClose(OnClose);
        }

        // The dialog's own event: Escape raises it, and the runtime raises it for a press outside.
        if (OnCancel.HasValue)
        {
            dialog = dialog.OnCancel(OnCancel);
        }

        return Div.Class("inline").Data(Marker.With(null))[dialog[Body(id)]];
    }

    private bool IsFlyout => Flyout == true || Variant == Ui.ModalVariant.Flyout;

    private bool IsBare => Variant == Ui.ModalVariant.Bare;

    private bool ScrollsBody => Scroll == Ui.ModalScroll.Body && !IsFlyout && !IsBare;

    // Flux's marks — the dialog's name, and that it is a flyout — then what the runtime is asked for: how it
    // is dismissed, the page held still behind it and, when the page owns the state, whether it is open.
    private Dictionary<string, string?> Marks(string id)
    {
        var marks = new Dictionary<string, string?>(StringComparer.Ordinal) { ["modal"] = id };

        if (IsFlyout)
        {
            marks["ui-flyout"] = null;
        }

        marks["rask-modal"] = DismissedBy();
        marks["rask-lock"] = "scroll";

        if (Open is not null || Name is null)
        {
            marks["rask-modal-open"] = Open == false ? "false" : "true";
        }

        return marks;
    }

    private string DismissedBy() => (Dismissible != false, Escapable != false) switch
    {
        (true, true) => "any",
        (true, false) => "press",
        (false, true) => "escape",
        _ => "none",
    };

    // Flux's order: the focus placeholder, the content, the close button in the corner.
    private Component Body(string id)
    {
        Component content = Context.Provide(new UiModalScope(id))[Children ?? []];

        if (IsBare)
        {
            return [FocusPlaceholder(), content];
        }

        Component?[] parts =
        [
            content,
            Closable == false
                ? null
                : Div.Class("absolute end-0 top-0 me-4 mt-4")[
                    Div.Class("inline").Data(CloseMarker.With(null))[CloseButton(id)]
                ]
        ];

        if (!ScrollsBody)
        {
            return [FocusPlaceholder(), .. parts];
        }

        // The layer scrolls, so the dialog fills the viewport and nothing is outside it: the panel is a box
        // inside, and the room around it is a button that asks the dialog to close as a press outside would.
        return
        [
            FocusPlaceholder(),
            Div.Class("relative flex min-h-full items-start justify-center p-4 sm:p-12")[
                Dismissible == false
                    ? null
                    : Outside().Class("absolute inset-0 size-full cursor-default border-0 bg-transparent").Attributes(UiModalInvoker.Dismisses(id)),
                Div.Class(UiClass.Compose("relative", Panel, Box, Class))[parts]
            ]
        ];
    }

    // Takes the focus a dialog would otherwise give its first field, then leaves: nothing is ringed when it
    // opens, and the first Tab lands on the first control (ui.css hides it once it has done that).
    private static HTMLDivElement FocusPlaceholder() =>
        Div.TabIndex(-1).Attributes(("data-ui-focus-placeholder", null), ("autofocus", null));

    // Flux's own button, as Flux draws its corner: subtle, small, the mini cross — and a shade lighter at
    // rest in light mode than a subtle button is, which only important utilities can say over the button's own.
    private static UiButton CloseButton(string id) =>
        Ui.Button.Subtle.Sm.Icon(Ui.IconName.XMark).IconVariant(Ui.IconVariant.Mini).AriaLabel(RaskStrings.Get(RaskString.ModalClose, "Close modal"))
            .Class("text-zinc-400! hover:text-zinc-800! dark:text-zinc-500! dark:hover:text-white!")
            .Attributes(UiModalInvoker.Closes(id));

    // What a press outside lands on where the dialog's own box is the whole viewport. Never in the tab order
    // or the accessibility tree: the close button and Escape are the keyboard's ways out.
    private static HTMLButtonElement Outside() =>
        Button.Type(ButtonType.Button).TabIndex(-1).Aria("hidden", "true");

    private string Shape()
    {
        if (ScrollsBody)
        {
            return "m-0 size-full max-h-none max-w-none overflow-y-auto border-0 bg-transparent p-0 text-inherit";
        }

        if (IsBare)
        {
            return UiClass.Compose(Bare, Grows);
        }

        if (!IsFlyout)
        {
            return UiClass.Compose(Panel, Box, Grows);
        }

        var position = Position ?? Ui.ModalPosition.Right;

        return Variant == Ui.ModalVariant.Floating
            ? UiClass.Compose(Panel, Floating, FloatingExtent(position), FloatingEdge(position), Slide(position))
            : UiClass.Compose(Sheet, Extent(position), Edge(position), Slide(position));
    }

    // Held to its edge, and the margin on the page's side gives way; the line is on the side that faces the page.
    private static string Edge(Ui.ModalPosition position) => position switch
    {
        Ui.ModalPosition.Left => "m-0 me-auto border-e",
        Ui.ModalPosition.Bottom => "m-0 mt-auto border-t",
        _ => "m-0 ms-auto border-s",
    };

    private static string FloatingEdge(Ui.ModalPosition position) => position switch
    {
        Ui.ModalPosition.Left => "m-2 me-auto",
        Ui.ModalPosition.Bottom => "m-2 mt-auto",
        _ => "m-2 ms-auto",
    };

    // Where it comes in from, and leaves to.
    private static string Slide(Ui.ModalPosition position) => position switch
    {
        Ui.ModalPosition.Left => "[transform:scale(1)_translateX(-50px)] starting:open:[transform:scale(1)_translateX(-50px)]",
        Ui.ModalPosition.Bottom => "[transform:scale(1)_translateY(50px)] starting:open:[transform:scale(1)_translateY(50px)]",
        _ => "[transform:scale(1)_translateX(50px)] starting:open:[transform:scale(1)_translateX(50px)]",
    };

    // A side flyout is the viewport's height and at least 25rem wide from md, unless the call site says; a bottom one its width.
    private static string Extent(Ui.ModalPosition position) =>
        position == Ui.ModalPosition.Bottom
            ? "max-h-dvh w-full max-w-none"
            : "max-h-dvh min-h-dvh md:[:where(&)]:min-w-[25rem]";

    private static string FloatingExtent(Ui.ModalPosition position) =>
        position == Ui.ModalPosition.Bottom
            ? "max-h-[calc(100dvh-1rem)] w-[calc(100%-1rem)] max-w-none"
            : "max-h-[calc(100dvh-1rem)] min-h-[calc(100dvh-1rem)] md:[:where(&)]:min-w-[25rem]";
}
