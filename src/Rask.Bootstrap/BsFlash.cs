using Rask.Core.Messaging;

namespace Rask.Bootstrap;

// Ready-made Bootstrap surface for Rask's flash (Rails' flash). Wraps the headless core FlashOutlet:
// it drains the scoped IFlash and renders each message as a BsToast in a fixed toast-container, mapping
// FlashLevel onto Bootstrap colour + icon. Mount ONE in the app layout (persists across client-side
// navigations); a message queued before a NavigateTo shows once on arrival.
//
//   • placement  — the toast-container's corner (Bootstrap position utilities); default top-end.
//   • auto-hide  — optional; each BsToast dismisses itself after AutoHideMs (its own one-shot timer),
//                  calling back into the outlet's dismiss. Null (default) = sticky until the × is clicked.
//
// The dismiss callback handed to the template removes the message from the outlet by Id; wiring it to
// BsToast.OnClose covers both the × button and the auto-hide timer. FlashOutlet.Dismiss re-renders the
// outlet itself, so no manual StateHasChanged is needed here.
public sealed class BsFlash : Component
{
    // Bootstrap position utilities placing the fixed toast-container. Default: top-right, the
    // conventional toast corner. Override for e.g. "bottom-0 start-0".
    public string Placement { get; set; } = "top-0 end-0";

    // Auto-dismiss each toast after this many ms. Null = sticky (dismiss via the × only).
    public int? AutoHideMs { get; set; }

    protected override RenderResult Render() =>
        FlashOutlet(Template: (messages, dismiss) =>
            Div(Class: $"toast-container position-fixed {Placement} p-3")[
                messages.Select(m => (Child)BsToast(
                    Id: m.Id,
                    Title: m.Title,
                    Message: m.Message,
                    Color: ToColor(m.Level),
                    Icon: ToIcon(m.Level),
                    AutoHideMs: AutoHideMs,
                    OnClose: id => dismiss(id),
                    Key: m.Id.ToString()))
            ]);

    private static BsColor ToColor(FlashLevel level) => level switch
    {
        FlashLevel.Success => BsColor.Success,
        FlashLevel.Warning => BsColor.Warning,
        FlashLevel.Error => BsColor.Danger,
        _ => BsColor.Info
    };

    private static BsIconName ToIcon(FlashLevel level) => level switch
    {
        FlashLevel.Success => BsIconName.CheckCircleFill,
        FlashLevel.Warning => BsIconName.ExclamationTriangleFill,
        FlashLevel.Error => BsIconName.ExclamationOctagonFill,
        _ => BsIconName.InfoCircleFill
    };
}
