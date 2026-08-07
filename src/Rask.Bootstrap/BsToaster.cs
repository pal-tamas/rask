using Rask.Core.Messaging;

namespace Rask.Bootstrap;

// Ready-made Bootstrap surface for Rask's toasts (a flash-message pattern). Wraps the headless core ToastOutlet:
// it drains the scoped IToaster and renders each message as a BsToast in a fixed toast-container, mapping
// ToastLevel onto Bootstrap colour + icon. Mount ONE in the app layout (persists across client-side
// navigations); a message queued before a NavigateTo shows once on arrival.
//
//   • placement  — the toast-container's corner (Bootstrap position utilities); default top-end.
//   • auto-hide  — each BsToast dismisses itself after AutoHideMs (its own one-shot timer), calling back
//                  into the outlet's dismiss. Default 5000 ms; set null (or <= 0) to keep toasts sticky
//                  until the × is clicked.
//
// The dismiss callback handed to the template removes the message from the outlet by Id; wiring it to
// BsToast.OnClose covers both the × button and the auto-hide timer. ToastOutlet.Dismiss re-renders the
// outlet itself, so no manual StateHasChanged is needed here.
public sealed partial class BsToaster : Component
{
    // Bootstrap position utilities placing the fixed toast-container. Default: top-right, the
    // conventional toast corner. Override for e.g. "bottom-0 start-0".
    public string Placement { get; set; } = "top-0 end-0";

    // Auto-dismiss each toast after this many ms. Default 5000; null (or <= 0) = sticky (dismiss via the × only).
    public int? AutoHideMs { get; set; } = 5000;

    protected override Component? Render() =>
        ToastOutlet(Template: (messages, dismiss) =>
            Div(Class: $"toast-container position-fixed {Placement} p-3")[
                messages.Select(m => (Component)BsToast(
                    Id: m.Id,
                    Title: m.Title,
                    Message: m.Message,
                    Color: ToColor(m.Level),
                    Icon: ToIcon(m.Level),
                    AutoHideMs: AutoHideMs,
                    OnClose: id => dismiss(id),
                    Key: m.Id.ToString()))
            ]);

    private static BsColor ToColor(ToastLevel level) => level switch
    {
        ToastLevel.Success => BsColor.Success,
        ToastLevel.Warning => BsColor.Warning,
        ToastLevel.Error => BsColor.Danger,
        _ => BsColor.Info
    };

    private static BsIconName ToIcon(ToastLevel level) => level switch
    {
        ToastLevel.Success => BsIconName.CheckCircleFill,
        ToastLevel.Warning => BsIconName.ExclamationTriangleFill,
        ToastLevel.Error => BsIconName.ExclamationOctagonFill,
        _ => BsIconName.InfoCircleFill
    };
}
