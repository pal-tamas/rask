namespace Rask.Bootstrap;

// A Bootstrap offcanvas panel, driven by Rask's live runtime (no JS). The panel stays in the DOM and
// slides in when Open (the .show class); a .offcanvas-backdrop is rendered while open (unless Backdrop
// is false). Wire OnClose to dismiss.
//
// Set Responsive to a breakpoint to make it a responsive offcanvas (.offcanvas-{bp}): below the
// breakpoint it behaves as a slide-in drawer (Open/backdrop apply); at and above it Bootstrap renders
// the panel inline and static — the canonical pattern for a sidebar that collapses to a hamburger on
// mobile but sits in the layout on desktop. The header and backdrop are hidden at/above the breakpoint
// so the static panel carries no drawer chrome.
public sealed class BsOffcanvas : BsBlock
{
    public bool? Open { get; set; }
    public BsPlacement? Placement { get; set; }
    public Bp? Responsive { get; set; }
    public new string? Title { get; set; }
    public bool? HideClose { get; set; }

    // Renders the dimming backdrop while open (default true).
    public bool? Backdrop { get; set; }

    public Callback? OnClose { get; set; }
    public CallbackAsync? OnCloseAsync { get; set; }

    protected override Component? Render()
    {
        var open = Open is true;
        var placementCls = (Placement ?? BsPlacement.Start) switch
        {
            BsPlacement.End => "offcanvas-end",
            BsPlacement.Top => "offcanvas-top",
            BsPlacement.Bottom => "offcanvas-bottom",
            _ => "offcanvas-start",
        };

        // .offcanvas (always a drawer) vs .offcanvas-{bp} (drawer below the breakpoint, static above).
        var baseCls = Responsive is { } rbp ? $"offcanvas-{rbp.Token()}" : "offcanvas";
        // Hide drawer chrome (header, backdrop) at/above the breakpoint where the panel turns static.
        var hideAbove = Responsive is { } hbp ? Display.None(hbp) : null;

        var showHeader = Title is not null || HideClose is not true;
        var panel = Div(Id: Id, Class: BsClass.Join(baseCls, placementCls, open ? "show" : null, Class),
            TabIndex: -1, Role: "dialog")[
                showHeader
                    ? Div(Class: BsClass.Join("offcanvas-header", hideAbove))[
                        Title is not null ? H5(Class: "offcanvas-title")[Title] : null,
                        HideClose is not true
                            ? BsCloseButton(OnClick: OnClose, OnClickAsync: OnCloseAsync)
                            : null]
                    : null,
                Div(Class: "offcanvas-body")[Items]];

        return open && Backdrop is not false
            ? [panel,
                Div(Class: BsClass.Join("offcanvas-backdrop", "fade", "show", hideAbove),
                    OnClick: OnClose, OnClickAsync: OnCloseAsync)]
            : panel;
    }
}
