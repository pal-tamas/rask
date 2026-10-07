using System.ComponentModel;
using Rask.Core.Messaging;

namespace Rask;

public sealed partial class UiToaster
{
    /// <summary>
    ///     The kit's look for the session's toasts — what the host draws <c>Toast.Success(…)</c> with when the app
    ///     mounts no <c>ToastOutlet</c> of its own.
    /// </summary>
    /// <remarks>Machinery: the hosts hand this to the outlet they mount; an app writes its own template instead.</remarks>
    /// <param name="messages">The toasts showing now.</param>
    /// <param name="dismiss">Removes one by id.</param>
    /// <param name="options">Where they stack.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static Rask.Core.Component Messages(
        IReadOnlyList<ToastMessage> messages, Action<int> dismiss, ToastOptions options)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(dismiss);
        ArgumentNullException.ThrowIfNull(options);
        return Ui.Toaster.Position(options.Position).Align(options.Align)[
            messages.Select(m => Ui.Toast.Key(m.Id).Message(m.Message).Title(m.Title).Tone(ToneOf(m.Level))
                .Action(m.Action is { } action ? ActionButton(action, () => dismiss(m.Id)) : null)
                .OnDismiss(() => dismiss(m.Id)))];
    }

    private static Rask.Core.Component ActionButton(ToastAction action, Action dismiss) =>
        Ui.Button.Ghost.OnClick(async () =>
        {
            await action.Run.Invoke();
            dismiss();
        })[action.Label];

    private static Ui.Tone ToneOf(ToastLevel level) => level switch
    {
        ToastLevel.Success => Ui.Tone.Success,
        ToastLevel.Warning => Ui.Tone.Warning,
        ToastLevel.Error => Ui.Tone.Error,
        _ => Ui.Tone.Info,
    };
}
