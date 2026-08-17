namespace Rask.Bootstrap;

// A Bootstrap modal driven entirely by Rask's live runtime — no bootstrap.js. When Open, it renders
// the .modal.show (display:block) + a .modal-backdrop and is dismissed by wiring OnClose to your
// state. Click-outside-to-close works without JS: the outer .modal carries OnClose while a no-op
// "shield" handler on .modal-dialog stops inner clicks from bubbling to it (Rask invokes only the
// nearest data-rask-on-click handler). Set StaticBackdrop to disable outside-click dismissal.

/// <summary>
///     A modal dialog. It traps focus and closes on <c>Escape</c>, so it is a real dialog rather than a
///     floating panel — keep <c>Title</c> set, since that is its accessible name.
/// </summary>
public sealed partial class BsModal : BsBlock
{
    /// <summary>
    ///     Whether the modal is shown. Drive it from your own state and close it in <c>OnClose</c>.
    /// </summary>
    public bool? Open { get; set; }

    /// <summary>The dialog's heading, and its accessible name.</summary>
    public new string? Title { get; set; }

    /// <summary>Makes the dialog narrower or wider than the default.</summary>
    public BsSize? Size { get; set; }

    /// <summary>Centres the dialog vertically instead of anchoring it near the top.</summary>
    public bool? Centered { get; set; }

    /// <summary>Scrolls the body inside the dialog, keeping the header and footer in place.</summary>
    public bool? Scrollable { get; set; }

    // Full-screen dialog (edge-to-edge, no margins/border-radius). Fullscreen=true is full-screen at
    // every width (.modal-fullscreen); FullscreenBelow makes it full-screen only below the given
    // breakpoint (.modal-fullscreen-{bp}-down, e.g. Bp.Sm for phones) and supersedes Fullscreen when
    // both are set. Composes with Size: the dialog is sized at/above the breakpoint, full-screen below.

    /// <summary>Fills the viewport.</summary>
    public bool? Fullscreen { get; set; }

    /// <summary>
    ///     Fills the viewport only below this breakpoint — the usual way to make a dialog full-screen on
    ///     phones.
    /// </summary>
    public Bp? FullscreenBelow { get; set; }

    /// <summary>
    ///     Keeps the dialog open when the backdrop is clicked, for a step the user must answer rather than
    ///     dismiss.
    /// </summary>
    public bool? StaticBackdrop { get; set; }

    /// <summary>Hides the header's close button. Leave the user another way out.</summary>
    public bool? HideClose { get; set; }

    // Optional footer content (e.g. action buttons) placed in .modal-footer.

    /// <summary>The footer content, normally the actions.</summary>
    public new Component? Footer { get; set; }

    /// <summary>Runs when the dialog is dismissed — set your <c>Open</c> state to false here.</summary>
    public Action? OnClose { get; set; }

    /// <summary>Runs when the dialog is dismissed, asynchronously.</summary>
    public Func<Task>? OnCloseAsync { get; set; }

    // Shields clicks inside the dialog from the outer close handler (nearest-handler delegation).
    private static readonly Action Shield = () => { };

    protected override Component? Render()
    {
        if (Open is not true)
        {
            return null;
        }

        var dialogCls = BsClass.Join(
            "modal-dialog",
            FullscreenBelow is { } below ? $"modal-fullscreen-{below.Token()}-down"
                : Fullscreen is true ? "modal-fullscreen" : null,
            Centered is true ? "modal-dialog-centered" : null,
            Scrollable is true ? "modal-dialog-scrollable" : null,
            Size is { } s && s.Suffix() is { } suffix ? $"modal-{suffix}" : null);

        var staticBackdrop = StaticBackdrop is true;
        var showHeader = Title is not null || HideClose is not true;

        // Label the dialog for assistive tech: reference the visible title by id when we have one to
        // anchor to, otherwise fall back to aria-label carrying the title text.
        var titleId = Title is not null && Id is not null ? Id + "-title" : null;
        var aria = new Dictionary<string, string?> { ["modal"] = "true" };
        if (titleId is not null)
        {
            aria["labelledby"] = titleId;
        }
        else if (Title is not null)
        {
            aria["label"] = Title;
        }

        // Opt into the runtime focus trap (autofocus in, Tab cycling, focus restore on close, Escape to
        // dismiss). data-rask-dismiss marks the click target Escape fires — the modal itself, whose
        // backdrop-close handler is OnClose — and is omitted for a static backdrop, which (per Bootstrap)
        // also disables Escape while keeping the close button working.
        var data = new Dictionary<string, string?> { ["rask-focus-trap"] = "" };
        if (!staticBackdrop)
        {
            data["rask-dismiss"] = "";
        }

        var content = Div.Class("modal-content")[
            showHeader
                ? Div.Class("modal-header")[
                    Title is not null ? H5.Id(titleId).Class("modal-title")[Title] : null,
                    HideClose is not true
                        ? BsCloseButton.OnClick(OnClose).OnClickAsync(OnCloseAsync)
                        : null]
                : null,
            Div.Class("modal-body")[Items],
            Footer is { } footer ? Div.Class("modal-footer")[footer] : null];

        var modal = Div
            .Id(Id)
            .Class("modal fade show")
            .Style("display:block")
            .TabIndex(-1)
            .Role("dialog")
            .Aria(aria)
            .Data(data)
            .OnClick(staticBackdrop ? null : OnClose)
            .OnClickAsync(staticBackdrop ? null : OnCloseAsync)[
                Div.Class(dialogCls).OnClick(staticBackdrop ? null : Shield)[content]];

        return [modal, Div.Class("modal-backdrop fade show")];
    }
}
