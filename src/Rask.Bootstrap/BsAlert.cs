namespace Rask.Bootstrap;

// A Bootstrap alert: <div class="alert alert-{color}" role="alert">. When Dismissible, a .btn-close
// button is appended; with zero JavaScript the close is wired to OnClose, which the parent uses to
// stop rendering the alert (state-driven, the Rask way) — Bootstrap's own dismiss JS is not used.

/// <summary>
///     A contextual feedback banner. Colour alone must not be the only signal — pair it with an icon or
///     wording, or a colour-blind reader loses the meaning. For a transient notification, use
///     <c>BsToast</c> instead.
/// </summary>
public sealed partial class BsAlert : BsBlock
{
    /// <summary>The semantic colour, which sets the meaning as much as the palette.</summary>
    public BsColor? Color { get; set; }

    // Renders a close button (.alert-dismissible + .btn-close). Pair with OnClose to remove the alert.

    /// <summary>Adds a close button that removes the alert.</summary>
    public bool? Dismissible { get; set; }

    /// <summary>Runs when the alert is dismissed.</summary>
    public Action? OnClose { get; set; }

    /// <summary>Runs when the alert is dismissed, asynchronously.</summary>
    public Func<Task>? OnCloseAsync { get; set; }

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
            return Div.Id(Id).Class(cls).Role("alert")[Items];
        }

        return Div.Id(Id).Class(cls).Role("alert")[
            ItemsWith(BsCloseButton.OnClick(OnClose).OnClickAsync(OnCloseAsync))];
    }
}
