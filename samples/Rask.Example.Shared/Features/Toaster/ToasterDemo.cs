using Rask.Core.Messaging;

namespace Rask.Example.Shared.Features;

// Producer + display of Rask's toasts (a flash-message pattern). A component injects IToaster through the
// ctor and raises a message; the headless ToastOutlet — subscribed to IToaster.Changed — drains it and
// hands it to this Template, rendered here as a dismissible stack. In a real app you mount ONE outlet in
// the layout and raise toasts just before Navigator.NavigateTo(...): scoped per session, the message
// survives the client-side navigation and shows once on the destination. Shown inline here so the demo
// is contained.
//
// The subject is IToaster and ToastOutlet, both of which live in the framework — the Bs* components this
// used to render with were only its chrome, so it is rewritten in utilities rather than dropped.
public sealed partial class ToasterDemo(IToaster toast) : Component
{
    protected override Component? Render() =>
        Div[
            Div.Class("flex flex-wrap gap-2")[
                Button.Type("button").Class(Ui.BtnInfo)
                    .OnClick(() => toast.Info("Just so you know.", "Info"))["Info"],
                Button.Type("button").Class(Ui.BtnSuccess)
                    .OnClick(() => toast.Success("Your changes were saved.", "Saved"))["Success"],
                Button.Type("button").Class(Ui.BtnWarning)
                    .OnClick(() => toast.Warning("Double-check your input.", "Heads up"))["Warning"],
                Button.Type("button").Class(Ui.BtnDanger)
                    .OnClick(() => toast.Error("Something went wrong.", "Error"))["Error"]
            ],

            // The display half. A real app would mount one fixed toast container in the layout; here an
            // inline ToastOutlet keeps the messages inside the demo card. AutoDismissAfter makes each
            // message clear itself after 5s, and the × still dismisses it early.
            Div.Class("mt-3 flex flex-col gap-2")[
                ToastOutlet
                    .Template((messages, dismiss) =>
                        Div.Class("flex flex-col gap-2")[
                            messages.Select(m => Div
                                .Key(m.Id.ToString())
                                .Role("alert")
                                .Class($"{Tone(m.Level)} flex items-center gap-2")[
                                m.Title is { } title ? Strong[$"{title}:"] : null,
                                Span.Class("grow")[m.Message],
                                // The dismiss affordance is a real button with a name: the glyph alone
                                // would announce as nothing, and the journey clicks it by that name.
                                Button
                                    .Type("button")
                                    .Class("shrink-0 rounded px-1 leading-none opacity-60 hover:opacity-100")
                                    .Aria(DismissAria)
                                    .OnClick(() => dismiss(m.Id))["×"]
                            ])
                        ])
                    .AutoDismissAfter(TimeSpan.FromSeconds(5))
            ]
        ];

    private static readonly IReadOnlyDictionary<string, string?> DismissAria =
        new Dictionary<string, string?>(StringComparer.Ordinal) { ["label"] = "Dismiss" };

    private static string Tone(ToastLevel level) => level switch
    {
        ToastLevel.Success => Ui.AlertSuccess,
        ToastLevel.Warning => Ui.AlertWarning,
        ToastLevel.Error => Ui.AlertDanger,
        _ => Ui.AlertInfo
    };
}
