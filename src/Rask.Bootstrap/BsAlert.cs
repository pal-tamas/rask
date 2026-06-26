namespace Rask.Bootstrap;

// A Bootstrap alert: <div class="alert alert-{color}" role="alert">. When Dismissible, a .btn-close
// button is appended; with zero JavaScript the close is wired to OnClose, which the parent uses to
// stop rendering the alert (state-driven, the Rask way) — Bootstrap's own dismiss JS is not used.
public sealed class BsAlert : BsBlock
{
    public BsColor? Color { get; set; }

    // Renders a close button (.alert-dismissible + .btn-close). Pair with OnClose to remove the alert.
    public bool? Dismissible { get; set; }

    public Callback? OnClose { get; set; }
    public CallbackAsync? OnCloseAsync { get; set; }

    private static readonly IReadOnlyDictionary<string, string?> CloseAria =
        new Dictionary<string, string?> { ["label"] = "Close" };

    protected override RenderResult Render()
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

        // Forward only the handler the consumer set (both at once is RASK027).
        var close = OnCloseAsync is not null
            ? Button(Type: "button", Class: "btn-close", Aria: CloseAria, OnClickAsync: OnCloseAsync)
            : Button(Type: "button", Class: "btn-close", Aria: CloseAria, OnClick: OnClose);
        return Div(Id: Id, Class: cls, Role: "alert")[ItemsWith(close)];
    }
}
