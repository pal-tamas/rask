using Rask.Core.Messaging;
using static Rask.Markup;

namespace Rask.Core.Components;

// Rask's own look for toasts, for an app with the UI kit off: small, legible, out of the way. Inline styles — Core
// takes no styling dependency, as DefaultErrorPage does not.
internal static class DefaultToasts
{
    private const string BoxStyle =
        "pointer-events:auto;display:flex;gap:.75rem;align-items:flex-start;min-width:16rem;max-width:28rem;"
        + "padding:.75rem 1rem;border-radius:.5rem;box-shadow:0 4px 16px rgb(0 0 0/.18);"
        + "font:14px/1.4 system-ui,sans-serif;color:#fff;";

    private const string ButtonStyle =
        "margin-left:auto;background:none;border:1px solid rgb(255 255 255/.6);color:inherit;"
        + "border-radius:.375rem;padding:.125rem .5rem;font:inherit;cursor:pointer;";

    private const string CloseStyle =
        "background:none;border:none;color:inherit;font:inherit;cursor:pointer;opacity:.8;padding:0 .125rem;";

    /// <summary>Draws <paramref name="messages" /> stacked against the given edge and alignment.</summary>
    /// <param name="messages">The toasts showing now.</param>
    /// <param name="dismiss">Removes one by id.</param>
    /// <param name="top">Stack against the top edge rather than the bottom.</param>
    /// <param name="align">"start", "center" or "end".</param>
    internal static Component Render(IReadOnlyList<ToastMessage> messages, Action<int> dismiss, bool top, string align) =>
        Div.Style(StackStyle(top, align)).Role("region").Aria("label", "Notifications")[
            messages.Select(m => Div.Key(m.Id).Style(BoxStyle + "background:" + Colour(m.Level) + ";")
                .Role(m.Level == ToastLevel.Error ? "alert" : "status")[
                    Div[
                        m.Title is { } title ? Strong.Style("display:block;")[title] : null,
                        m.Message
                    ],
                    m.Action is { } action
                        ? Button.Type(ButtonType.Button).Style(ButtonStyle).OnClick(async () =>
                        {
                            await action.Run.Invoke();
                            dismiss(m.Id);
                        })[action.Label]
                        : null,
                    Button.Type(ButtonType.Button).Style(CloseStyle).Aria("label", "Dismiss").OnClick(() => dismiss(m.Id))["×"]
                ])
        ];

    private static string StackStyle(bool top, string align) =>
        "position:fixed;z-index:1000;pointer-events:none;display:flex;flex-direction:column;gap:.5rem;padding:1rem;"
        + (top ? "top:0;" : "bottom:0;")
        + align switch
        {
            "start" => "left:0;align-items:flex-start;",
            "center" => "left:50%;transform:translateX(-50%);align-items:center;",
            _ => "right:0;align-items:flex-end;",
        };

    private static string Colour(ToastLevel level) => level switch
    {
        ToastLevel.Success => "#15803d",
        ToastLevel.Warning => "#b45309",
        ToastLevel.Error => "#b91c1c",
        _ => "#1d4ed8",
    };
}
