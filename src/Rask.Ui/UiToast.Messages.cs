using System.ComponentModel;
using Rask.Core.Messaging;

namespace Rask;

public sealed partial class UiToast
{
    // A native popover, so a toast is in the top layer — over an open dialog — with the browser's own box for
    // one taken away. The runtime shows it (data-rask-popover-open): a render can only write attributes.
    private const string Host = "fixed inset-0 overflow-visible border-0 bg-transparent p-0";

    /// <summary>
    ///     The session's toasts in the kit's look — what the host draws <c>Toast.Success(…)</c> with when the app
    ///     places no <see cref="UiToast" /> of its own.
    /// </summary>
    /// <remarks>Machinery: the hosts hand this to the outlet they mount; an app places <c>Ui.Toast</c> instead.</remarks>
    /// <param name="messages">The toasts showing now, oldest first.</param>
    /// <param name="dismiss">Removes one by id.</param>
    /// <param name="options">Where they appear and how long they stay.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static Rask.Core.Component Messages(
        IReadOnlyList<ToastMessage> messages, Action<int> dismiss, ToastOptions options)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(dismiss);
        ArgumentNullException.ThrowIfNull(options);
        return Draw(messages, dismiss, new UiToastLook(options.Position, false, null, options.Duration, null));
    }

    // THE HOST IS ALWAYS ON THE PAGE, closed and empty while there is nothing to show — as Flux's <ui-toast> is
    // (measured on fluxui.dev/components/toast: `popover="manual"`, `role="status"`, not open, one <template>
    // inside; a toast is stamped into it and taken out again). So a toast arriving, leaving or taking another's
    // place is a change INSIDE an element: its dialogs are keyed, and the live diff ships each as an insert or a
    // removal. Drawn only while a toast showed, the host itself came and went among the layout's children, and a
    // change by position is answered with the whole document — on every save and every delete that says so.
    internal static Rask.Core.Component Draw(IReadOnlyList<ToastMessage> messages, Action<int> dismiss, UiToastLook look)
    {
        if (messages.Count == 0)
        {
            // No classes: `flex` on a closed popover would lay a transparent box over the whole page.
            return Div.Popover(Rask.Core.Popover.Manual)
                .Data(Marks(look.Stack is null ? "ui-toast" : "ui-toast-group", look.Position, expanded: false, open: false)).Role("status");
        }

        return look.Stack is { } stack ? Stacked(messages, dismiss, look, stack) : Single(messages, dismiss, look);
    }

    // One at a time: the newest takes the place of the one showing, and dismissing it takes those with it.
    private static Rask.Core.Component Single(IReadOnlyList<ToastMessage> messages, Action<int> dismiss, UiToastLook look)
    {
        var newest = messages[^1];
        void Close()
        {
            foreach (var message in messages)
            {
                dismiss(message.Id);
            }
        }

        // The dialog is keyed by the toast: a new one is a new node, so its entrance plays and its countdown is its own.
        return Div.Popover(Rask.Core.Popover.Manual).Class(Host, "max-w-sm", Corner(look.Position))
            .Data(Marks("ui-toast", look.Position, expanded: false)).Role("status")[
                Dialog(newest, Close, look, ahead: null, behind: 0)
            ];
    }

    // Newest first: each older toast is placed against the one in front of it, and CSS can only anchor to
    // an element that comes earlier in the tree.
    private static Rask.Core.Component Stacked(
        IReadOnlyList<ToastMessage> messages, Action<int> dismiss, UiToastLook look, UiToastStack stack)
    {
        var dialogs = new Rask.Core.Component[messages.Count];
        for (var behind = 0; behind < dialogs.Length; behind++)
        {
            var message = messages[^(behind + 1)];
            dialogs[behind] = Dialog(
                message, () => dismiss(message.Id), look, behind == 0 ? null : messages[^behind].Id, behind);
        }

        // One pointer over any of the stack holds every countdown in it (data-rask-dismiss-scope), and each
        // runs on from where it stopped: measured on Flux, two toasts hovered for five seconds both stayed,
        // and went their own remainders after the pointer left.
        var marks = Marks("ui-toast-group", look.Position, stack.Expanded);
        marks["rask-dismiss-scope"] = null;

        return Div.Popover(Rask.Core.Popover.Manual).Class(Host, "flex w-sm", Corner(look.Position), Justify(look.Position), stack.Class)
            .Data(marks).Role("status")[dialogs];
    }

    // The part's marker, then what Flux's custom element carries as attributes, as data-*.
    private static Dictionary<string, string?> Marks(string marker, Ui.ToastPosition position, bool expanded, bool open = true)
    {
        var marks = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [marker] = null,
            ["position"] = Name(position),
            // The runtime shows the popover when this turns true and hides it when it turns false.
            ["rask-popover-open"] = open ? "true" : "false",
        };
        if (expanded)
        {
            marks["expanded"] = null;
        }

        return marks;
    }

    // Flux's own words for the six corners.
    private static string Name(Ui.ToastPosition position) => position switch
    {
        Ui.ToastPosition.BottomCenter => "bottom center",
        Ui.ToastPosition.BottomStart => "bottom start",
        Ui.ToastPosition.TopEnd => "top end",
        Ui.ToastPosition.TopCenter => "top center",
        Ui.ToastPosition.TopStart => "top start",
        _ => "bottom end",
    };

    // 24px from the two edges it is against, and pushed to them by an auto margin on the other two.
    private static string Corner(Ui.ToastPosition position) => position switch
    {
        Ui.ToastPosition.BottomCenter => "m-6 mx-auto mt-auto",
        Ui.ToastPosition.BottomStart => "m-6 me-auto mt-auto",
        Ui.ToastPosition.TopEnd => "m-6 ms-auto mb-auto",
        Ui.ToastPosition.TopCenter => "m-6 mx-auto mb-auto",
        Ui.ToastPosition.TopStart => "m-6 me-auto mb-auto",
        _ => "m-6 ms-auto mt-auto",
    };

    // Where a stacked toast, narrower than its group on a phone, sits across it.
    private static string Justify(Ui.ToastPosition position) => position switch
    {
        Ui.ToastPosition.BottomCenter or Ui.ToastPosition.TopCenter => "justify-center",
        Ui.ToastPosition.BottomStart or Ui.ToastPosition.TopStart => "justify-start",
        _ => "justify-end",
    };
}
