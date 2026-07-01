using Rask.Core.Messaging;

namespace Rask.Example.Shared.Features;

// Producer + display of Rask's flash (Rails' flash). A component injects IFlash through the ctor and
// raises a message; the headless FlashOutlet — subscribed to IFlash.Changed — drains it and hands it to
// this Template, rendered here as a dismissible BsAlert stack. In a real app you mount ONE BsFlash in the
// layout and raise flashes just before Navigator.NavigateTo(...): scoped per session, the message survives
// the client-side navigation and shows once on the destination. Shown inline here so the demo is contained.
public sealed class FlashDemo(IFlash flash) : Component
{
    protected override RenderResult Render() =>
        Div()[
            Div(Class: "d-flex flex-wrap gap-2")[
                BsButton(Color: BsColor.Info, OnClick: () => flash.Info("Just so you know.", "Info"))["Info"],
                BsButton(Color: BsColor.Success,
                    OnClick: () => flash.Success("Your changes were saved.", "Saved"))["Success"],
                BsButton(Color: BsColor.Warning,
                    OnClick: () => flash.Warning("Double-check your input.", "Heads up"))["Warning"],
                BsButton(Color: BsColor.Danger, OnClick: () => flash.Error("Something went wrong.", "Error"))["Error"]
            ],

            // The display half. A real app would use BsFlash (a fixed toast-container) mounted once in the
            // layout; here an inline FlashOutlet keeps the messages inside the demo card.
            Div(Class: "mt-3")[
                FlashOutlet(Template: (messages, dismiss) =>
                    Div()[
                        messages.Select(m => (Child)BsAlert(
                            Color: ToColor(m.Level),
                            Dismissible: true,
                            OnClose: () => dismiss(m.Id),
                            Class: "d-flex align-items-center",
                            Key: m.Id.ToString())[
                            m.Title is { } title ? Strong(Class: "me-1")[$"{title}:"] : (Child)Fragment(),
                            m.Message])
                    ])
            ]
        ];

    private static BsColor ToColor(FlashLevel level) => level switch
    {
        FlashLevel.Success => BsColor.Success,
        FlashLevel.Warning => BsColor.Warning,
        FlashLevel.Error => BsColor.Danger,
        _ => BsColor.Info
    };
}
