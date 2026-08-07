using Rask.Core.Messaging;

namespace Rask.Example.Shared.Features;

// Producer + display of Rask's toasts (a flash-message pattern). A component injects IToaster through the ctor and
// raises a message; the headless ToastOutlet — subscribed to IToaster.Changed — drains it and hands it to
// this Template, rendered here as a dismissible BsAlert stack. In a real app you mount ONE BsToaster in the
// layout and raise toasts just before Navigator.NavigateTo(...): scoped per session, the message survives
// the client-side navigation and shows once on the destination. Shown inline here so the demo is contained.
public sealed partial class ToasterDemo(IToaster toast) : Component
{
    protected override Component? Render() =>
        Div()[
            BsStack(Gap: 2, WrapItems: true)[
                BsButton(Color: BsColor.Info, OnClick: () => toast.Info("Just so you know.", "Info"))["Info"],
                BsButton(Color: BsColor.Success,
                    OnClick: () => toast.Success("Your changes were saved.", "Saved"))["Success"],
                BsButton(Color: BsColor.Warning,
                    OnClick: () => toast.Warning("Double-check your input.", "Heads up"))["Warning"],
                BsButton(Color: BsColor.Danger, OnClick: () => toast.Error("Something went wrong.", "Error"))["Error"]
            ],

            // The display half. A real app would use BsToaster (a fixed toast-container) mounted once in the
            // layout; here an inline ToastOutlet keeps the messages inside the demo card. AutoDismissAfter
            // makes each message clear itself after 5s (the × still dismisses it early).
            Div(Class: "mt-3")[
                ToastOutlet(AutoDismissAfter: TimeSpan.FromSeconds(5), Template: (messages, dismiss) =>
                    Div()[
                        messages.Select(m => (Component)BsAlert(
                            Color: ToColor(m.Level),
                            Dismissible: true,
                            OnClose: () => dismiss(m.Id),
                            Class: "d-flex align-items-center",
                            Key: m.Id.ToString())[
                            m.Title is { } title ? Strong(Class: "me-1")[$"{title}:"] : null,
                            m.Message])
                    ])
            ]
        ];

    private static BsColor ToColor(ToastLevel level) => level switch
    {
        ToastLevel.Success => BsColor.Success,
        ToastLevel.Warning => BsColor.Warning,
        ToastLevel.Error => BsColor.Danger,
        _ => BsColor.Info
    };
}
