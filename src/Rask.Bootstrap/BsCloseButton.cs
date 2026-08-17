namespace Rask.Bootstrap;

// A Bootstrap close button: <button class="btn-close" aria-label="Close">. Reused by BsAlert,
// BsModal, BsOffcanvas and BsToast for their dismiss control. Wire OnClick/OnClickAsync to drive
// the dismissal from C# (zero JS).

/// <summary>
///     The standard dismiss button used by alerts, modals, toasts and offcanvas panels. It has no visible
///     text, so it always needs an <c>AriaLabel</c>.
/// </summary>
public sealed partial class BsCloseButton : BsBlock
{
    // The btn-close-white variant for dark backgrounds.

    /// <summary>The light variant, for use on a dark background.</summary>
    public bool? White { get; set; }

    // Accessible label; defaults to "Close".

    /// <summary>The accessible name — the button has no text of its own. Defaults to "Close".</summary>
    public string? AriaLabel { get; set; }

    /// <summary>Makes the button unclickable.</summary>
    public bool? Disabled { get; set; }

    /// <summary>Runs when the button is clicked.</summary>
    public Action? OnClick { get; set; }

    /// <summary>Runs when the button is clicked, asynchronously.</summary>
    public Func<Task>? OnClickAsync { get; set; }

    protected override Component? Render()
    {
        var aria = new Dictionary<string, string?> { ["label"] = AriaLabel ?? "Close" };
        return Button
            .Id(Id)
            .Type("button")
            .Disabled(Disabled)
            .Class(BsClass.Join("btn-close", White is true ? "btn-close-white" : null, Class))
            .Aria(aria)
            .OnClick(OnClick)
            .OnClickAsync(OnClickAsync)[Items];
    }
}
