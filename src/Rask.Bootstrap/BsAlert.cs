namespace Rask.Bootstrap;

// A Bootstrap alert: <div class="alert alert-{color}" role="alert">. When Dismissible, a .btn-close
// button is appended; with zero JavaScript the close is wired to OnClose, which the parent uses to
// stop rendering the alert (state-driven, the Rask way) — Bootstrap's own dismiss JS is not used.
public sealed partial class BsAlert : BsBlock
{
    public BsColor? Color { get; set; }

    // Renders a close button (.alert-dismissible + .btn-close). Pair with OnClose to remove the alert.
    public bool? Dismissible { get; set; }

    public Handler? OnClose { get; set; }
    public HandlerAsync? OnCloseAsync { get; set; }

    protected override Component? Render()
    {
        var dismissible = Dismissible is true;
        var cls = BsClass.Join(
            "alert",
            Color is { } c ? c.Alert() : null,
            dismissible ? "alert-dismissible" : null,
            Class);

        if (!dismissible)
        {
            return Div(Id: Id, Class: cls, Role: "alert")[Items];
        }

        return Div(Id: Id, Class: cls, Role: "alert")[
            ItemsWith(BsCloseButton(OnClick: OnClose?.Fn, OnClickAsync: OnCloseAsync?.Fn))];
    }
}
