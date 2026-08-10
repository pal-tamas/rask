namespace Rask.Bootstrap;

// A Bootstrap close button: <button class="btn-close" aria-label="Close">. Reused by BsAlert,
// BsModal, BsOffcanvas and BsToast for their dismiss control. Wire OnClick/OnClickAsync to drive
// the dismissal from C# (zero JS).
public sealed partial class BsCloseButton : BsBlock
{
    // The btn-close-white variant for dark backgrounds.
    public bool? White { get; set; }

    // Accessible label; defaults to "Close".
    public string? AriaLabel { get; set; }

    public bool? Disabled { get; set; }

    public Handler? OnClick { get; set; }
    public HandlerAsync? OnClickAsync { get; set; }

    protected override Component? Render()
    {
        var aria = new Dictionary<string, string?> { ["label"] = AriaLabel ?? "Close" };
        return Button
            .Id(Id)
            .Type("button")
            .Disabled(Disabled)
            .Class(BsClass.Join("btn-close", White is true ? "btn-close-white" : null, Class))
            .Aria(aria)
            .OnClick(OnClick?.Fn)
            .OnClickAsync(OnClickAsync?.Fn)[Items];
    }
}
