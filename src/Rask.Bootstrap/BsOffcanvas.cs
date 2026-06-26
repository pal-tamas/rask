namespace Rask.Bootstrap;

// A Bootstrap offcanvas panel, driven by Rask's live runtime (no JS). The panel stays in the DOM and
// slides in when Open (the .show class); a .offcanvas-backdrop is rendered while open (unless Backdrop
// is false). Wire OnClose to dismiss.
public sealed class BsOffcanvas : BsBlock
{
    public bool? Open { get; set; }
    public BsPlacement? Placement { get; set; }
    public string? Title { get; set; }
    public bool? HideClose { get; set; }

    // Renders the dimming backdrop while open (default true).
    public bool? Backdrop { get; set; }

    public Callback? OnClose { get; set; }
    public CallbackAsync? OnCloseAsync { get; set; }

    protected override RenderResult Render()
    {
        var open = Open is true;
        var placementCls = (Placement ?? BsPlacement.Start) switch
        {
            BsPlacement.End => "offcanvas-end",
            BsPlacement.Top => "offcanvas-top",
            BsPlacement.Bottom => "offcanvas-bottom",
            _ => "offcanvas-start",
        };

        var showHeader = Title is not null || HideClose is not true;
        var panel = Div(Id: Id, Class: BsClass.Join("offcanvas", placementCls, open ? "show" : null, Class),
            TabIndex: -1, Role: "dialog")[
                showHeader
                    ? Div(Class: "offcanvas-header")[
                        Title is not null ? H5(Class: "offcanvas-title")[Title] : (Child)Fragment(),
                        HideClose is not true
                            ? BsCloseButton(OnClick: OnClose, OnClickAsync: OnCloseAsync)
                            : (Child)Fragment()]
                    : (Child)Fragment(),
                Div(Class: "offcanvas-body")[Items]];

        return open && Backdrop is not false
            ? Fragment()[panel, Div(Class: "offcanvas-backdrop fade show", OnClick: OnClose, OnClickAsync: OnCloseAsync)]
            : panel;
    }
}
