namespace Rask.Bootstrap;

// A Bootstrap modal driven entirely by Rask's live runtime — no bootstrap.js. When Open, it renders
// the .modal.show (display:block) + a .modal-backdrop and is dismissed by wiring OnClose to your
// state. Click-outside-to-close works without JS: the outer .modal carries OnClose while a no-op
// "shield" handler on .modal-dialog stops inner clicks from bubbling to it (Rask invokes only the
// nearest data-rask-on-click handler). Set StaticBackdrop to disable outside-click dismissal.
public sealed class BsModal : BsBlock
{
    public bool? Open { get; set; }
    public string? Title { get; set; }
    public BsSize? Size { get; set; }
    public bool? Centered { get; set; }
    public bool? Scrollable { get; set; }

    // Full-screen dialog (edge-to-edge, no margins/border-radius). Fullscreen=true is full-screen at
    // every width (.modal-fullscreen); FullscreenBelow makes it full-screen only below the given
    // breakpoint (.modal-fullscreen-{bp}-down, e.g. Bp.Sm for phones) and supersedes Fullscreen when
    // both are set. Composes with Size: the dialog is sized at/above the breakpoint, full-screen below.
    public bool? Fullscreen { get; set; }
    public Bp? FullscreenBelow { get; set; }

    public bool? StaticBackdrop { get; set; }
    public bool? HideClose { get; set; }

    // Optional footer content (e.g. action buttons) placed in .modal-footer.
    public new Component? Footer { get; set; }

    public Callback? OnClose { get; set; }
    public CallbackAsync? OnCloseAsync { get; set; }

    // Shields clicks inside the dialog from the outer close handler (nearest-handler delegation).
    private static readonly Callback Shield = () => { };

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

        var content = Div(Class: "modal-content")[
            showHeader
                ? Div(Class: "modal-header")[
                    Title is not null ? H5(Id: titleId, Class: "modal-title")[Title] : null,
                    HideClose is not true
                        ? BsCloseButton(OnClick: OnClose, OnClickAsync: OnCloseAsync)
                        : null]
                : null,
            Div(Class: "modal-body")[Items],
            Footer is { } footer ? Div(Class: "modal-footer")[footer] : null];

        var modal = Div(Id: Id, Class: "modal fade show", Style: "display:block", TabIndex: -1,
            Role: "dialog", Aria: aria, Data: data,
            OnClick: staticBackdrop ? null : OnClose, OnClickAsync: staticBackdrop ? null : OnCloseAsync)[
                Div(Class: dialogCls, OnClick: staticBackdrop ? null : Shield)[content]];

        return [modal, Div(Class: "modal-backdrop fade show")];
    }
}
