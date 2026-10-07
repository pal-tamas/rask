using System.Globalization;

namespace Rask;

/// <summary>
///     Flux's <c>flux:modal</c>: content in a layer above the page — a centred panel, or with
///     <see cref="Flyout" /> a sheet anchored to an edge.
/// </summary>
/// <remarks>
///     <para>
///     A real <c>&lt;dialog&gt;</c>. Give it a <see cref="Name" /> and a <see cref="UiModalTrigger" /> naming it,
///     and the browser owns the whole interaction: the trigger is an invoker (<c>command="show-modal"</c>),
///     so the dialog opens the way <c>showModal()</c> opens it — the top layer, the page behind made inert,
///     Escape, a click outside, and focus handed back to the trigger — with no handler and no runtime.
///     A browser without invoker commands (before Chrome 135, Firefox 144, Safari 26.2) opens it as a
///     <c>popover</c> instead.
///     </para>
///     <para>
///     Set <see cref="Open" /> and the page owns the state instead, which is Rask's <c>wire:model</c>: render
///     it open when something in C# decides it should be, and stop when <see cref="OnClose" /> says the reader
///     closed it. Nothing in markup can put an element in the top layer, so on this path it is an ordinary
///     <c>&lt;dialog open&gt;</c> over a backdrop the kit draws, held by the runtime's focus trap.
///     </para>
/// </remarks>
public sealed partial class UiModal : Component
{
    // What every variant shares: fading and settling in over 150ms, leaving in 75, and the page dimmed behind.
    private const string Motion =
        "opacity-0 transition-all transition-discrete duration-75 ease-[ease] open:opacity-100 open:duration-150 "
        + "starting:open:opacity-0 open:[transform:translate(0)_scale(1)] backdrop:bg-[rgba(0,0,0,0.1)] backdrop:opacity-0 "
        + "backdrop:transition-all backdrop:transition-discrete backdrop:duration-75 backdrop:ease-[ease] "
        + "open:backdrop:opacity-100 open:backdrop:duration-150 starting:open:backdrop:opacity-0 "
        + "open:backdrop:[transform:translate(0)_scale(1)]";

    private const string Grows = "[transform:scale(0.95)] starting:open:[transform:scale(0.95)]";

    // A light colour with a dark twin sits in :where(), here and below. An app's own sheet comes after the
    // kit's and emits `bg-white` too, which on equal terms would beat `dark:bg-zinc-800` in dark mode.
    private const string Panel =
        "border-0 text-inherit shadow-lg ring [:where(&)]:bg-white [:where(&)]:ring-black/5 dark:bg-zinc-800 dark:ring-zinc-700";

    private const string Box = "m-auto rounded-xl p-6 [:where(&)]:min-w-xs [:where(&)]:max-w-xl";

    // A sheet against an edge, with a line on its page side that only dark mode draws.
    private const string Sheet =
        "fixed overflow-y-auto p-8 text-inherit [:where(&)]:border-transparent [:where(&)]:bg-white "
        + "dark:border-zinc-700 dark:bg-zinc-800";

    private const string Floating = "fixed overflow-y-auto rounded-xl p-8";

    private const string Bare = "m-auto border-0 bg-transparent p-0 text-inherit";


    // The page owns the state, so the dialog is not in the top layer and has to place itself.
    private const string InPage =
        "fixed inset-0 z-50 overflow-auto [:where(&)]:max-h-[calc(100%-38px)] [:where(&)]:max-w-[calc(100%-38px)]";


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
    protected override Component? Render() =>
        Div.Class("inline").Attributes(("data-ui-modal", null))[
            Open is null && Name is not null ? Declarative(Name) : StateDriven()
        ];

    private bool IsFlyout => Flyout == true || Variant == Ui.ModalVariant.Flyout;

    private bool IsBare => Variant == Ui.ModalVariant.Bare;

    private bool ScrollsBody => Scroll == Ui.ModalScroll.Body && !IsFlyout && !IsBare;

    private Component Declarative(string name)
    {
        var dialog = Marked(Dialog.Id(name).Class(Classes(inPage: false)), ("closedby", ClosedBy()))
            // The fallback's light dismiss and Escape come as a pair, so it only has them when both are wanted.
            .Popover(Dismissible == false || Escapable == false ? Rask.Core.Popover.Manual : Rask.Core.Popover.Auto);

        if (OnClose.HasValue)
        {
            // The toggle rather than the close event: a popover the fallback opened raises no close.
            dialog = dialog.OnToggle(e =>
                string.Equals(e.NewState, "open", StringComparison.Ordinal) ? ValueTask.CompletedTask : OnClose.Invoke());
        }

        // The platform's own: Escape and a click outside raise it, a close button does not.
        if (OnCancel.HasValue)
        {
            dialog = dialog.OnCancel(OnCancel);
        }

        return dialog[
            Body(
                new UiModalScope(name, default),
                CloseButton().Attributes(UiModalInvoker.Closes(name))[Cross()],
                // The command closes it with no runtime; the handler only adds the word "dismissed".
                Outside().Attributes(UiModalInvoker.Closes(name)).OnClick(OnCancel))
        ];
    }

    private Component StateDriven()
    {
        var open = Open != false;
        (string, string?)[] held = open ? [("data-open", null), ("data-rask-focus-trap", null)] : [];
        var dialog = Marked(Dialog.Class(Classes(inPage: true)), held).Open(open);
        var body = Body(
            new UiModalScope(null, OnClose),
            CloseButton().OnClick(() => OnClose.Invoke())[Cross()],
            Outside().OnClick(DismissAsync));

        if (!open)
        {
            return dialog[body];
        }

        return
        [
            Backdrop(),
            dialog[
                body,
                // What the runtime presses on Escape. Not the close button: Escape is a dismissal.
                Escapable == false ? null : Outside().Class("hidden").Attributes(("data-rask-dismiss", null)).OnClick(DismissAsync)
            ]
        ];
    }

    // The dimmed page, which the top layer draws for a dialog the browser opened and nothing draws for this one.
    private Component Backdrop() =>
        Dismissible == false || ScrollsBody
            ? Div.Class("fixed inset-0 z-50 bg-black/10").Aria("hidden", "true")
            : Outside().Class("fixed inset-0 z-50 size-full cursor-default border-0 bg-black/10").OnClick(DismissAsync);

    // Flux's marks — the dialog's name, and that it is a flyout — and whatever this path adds to them.
    private HTMLDialogElement Marked(HTMLDialogElement dialog, params (string, string?)[] attributes)
    {
        var named = dialog.Data("modal", Name ?? "ui-modal-" + _instance.ToString(CultureInfo.InvariantCulture));
        (string, string?)[] marks = IsFlyout ? [("data-ui-flyout", null), .. attributes] : attributes;

        if (marks.Length != 0)
        {
            named = named.Attributes(marks);
        }

        return named;
    }


    // Flux's order: the focus placeholder, the content, the close button in the corner.
    private Component Body(UiModalScope scope, Component close, HTMLButtonElement outside)
    {
        Component content = Context.Provide(scope)[Children ?? []];

        if (IsBare)
        {
            return [FocusPlaceholder(), content];
        }

        Component?[] parts =
        [
            content,
            Closable == false

                ? null
                : Div.Class("absolute end-0 top-0 me-4 mt-4")[Div.Class("inline").Attributes(("data-ui-modal-close", null))[close]]
        ];

        if (!ScrollsBody)
        {
            return [FocusPlaceholder(), .. parts];
        }

        // The layer scrolls, so the panel is a box inside it and the room around it is what a click outside lands on.
        return
        [
            FocusPlaceholder(),
            Div.Class("relative flex min-h-full items-start justify-center [:where(&)]:p-4 sm:p-12")[
                Dismissible == false ? null : outside.Class("absolute inset-0 size-full cursor-default border-0 bg-transparent"),
                Div.Class(UiClass.Compose("relative", Panel, Box, Class))[parts]
            ]
        ];
    }

    // Takes the focus a dialog would otherwise give its first field, then leaves: nothing is ringed when it
    // opens, and the first Tab lands on the first control (ui.css hides it once it has done that).
    private static HTMLDivElement FocusPlaceholder() =>
        Div.TabIndex(-1).Attributes(("data-ui-focus-placeholder", null), ("autofocus", null));

    private static HTMLButtonElement CloseButton() =>
        Button
            .Type(ButtonType.Button)
            .Class(
                "relative inline-flex size-8 items-center justify-center gap-2 whitespace-nowrap rounded-md "
                + "border-0 bg-transparent p-0 text-sm font-medium [:where(&)]:text-zinc-400 hover:bg-zinc-800/5 "
                + "hover:text-zinc-800 dark:text-zinc-500 dark:hover:bg-white/15 dark:hover:text-white")

            .Aria("label", "Close modal");

    private static UiIcon Cross() => Ui.Icon.Name(Ui.IconName.XMark).Mini;

    // What a click outside lands on where the platform cannot hear one itself. Never in the tab order or the
    // accessibility tree: the close button and Escape are the keyboard's ways out.
    private static HTMLButtonElement Outside() =>
        Button.Type(ButtonType.Button).TabIndex(-1).Aria("hidden", "true");

    // Cancel first, then close: the order the platform raises them in.
    private async Task DismissAsync()
    {
        await OnCancel.Invoke().ConfigureAwait(true);

        await OnClose.Invoke().ConfigureAwait(true);
    }

    // `any` is Escape and a click outside, `closerequest` Escape alone. Nothing asks for a click outside
    // alone, so a dialog that is not escapable gives that up too.
    private string ClosedBy()
    {
        if (Escapable == false)
        {
            return "none";
        }

        return Dismissible == false || ScrollsBody ? "closerequest" : "any";
    }


    // A class from the call site goes on whatever draws the panel, which is the dialog unless the layer scrolls.
    private string Classes(bool inPage) =>
        UiClass.Compose(Motion, Shape(), inPage ? InPage : null, ScrollsBody ? null : Class);

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

    // Each side said once, never a shorthand with one side then taken back: an app's own sheet comes after
    // the kit's and emits `m-0` and `border-0` too, and there the shorthand would win the side back.
    // Three margins hold it to its edge and the fourth gives way; the line is on the side that faces the page.
    private static string Edge(Ui.ModalPosition position) => position switch
    {
        Ui.ModalPosition.Left => "my-0 ms-0 me-auto border-y-0 border-s-0 border-e",
        Ui.ModalPosition.Bottom => "mx-0 mb-0 mt-auto border-x-0 border-b-0 border-t",
        _ => "my-0 me-0 ms-auto border-y-0 border-e-0 border-s",
    };

    private static string FloatingEdge(Ui.ModalPosition position) => position switch
    {
        Ui.ModalPosition.Left => "my-2 ms-2 me-auto",
        Ui.ModalPosition.Bottom => "mx-2 mb-2 mt-auto",
        _ => "my-2 me-2 ms-auto",
    };

    // Where it comes in from, and leaves to.
    private static string Slide(Ui.ModalPosition position) => position switch
    {
        Ui.ModalPosition.Left => "[transform:scale(1)_translateX(-50px)] starting:open:[transform:scale(1)_translateX(-50px)]",
        Ui.ModalPosition.Bottom => "[transform:scale(1)_translateY(50px)] starting:open:[transform:scale(1)_translateY(50px)]",
        _ => "[transform:scale(1)_translateX(50px)] starting:open:[transform:scale(1)_translateX(50px)]",
    };


    // A side flyout is the viewport's height and at least 25rem wide from md; a bottom one its width.
    private static string Extent(Ui.ModalPosition position) =>
        position == Ui.ModalPosition.Bottom
            ? "max-h-dvh w-full max-w-none"
            : "max-h-dvh min-h-dvh md:[:where(&)]:min-w-[25rem]";

    private static string FloatingExtent(Ui.ModalPosition position) =>
        position == Ui.ModalPosition.Bottom
            ? "max-h-[calc(100dvh-1rem)] w-[calc(100%-1rem)] max-w-none"
            : "max-h-[calc(100dvh-1rem)] min-h-[calc(100dvh-1rem)] md:[:where(&)]:min-w-[25rem]";
}
