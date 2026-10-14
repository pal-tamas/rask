using System.Text;

namespace Rask.Core;

public abstract partial class Element
{
    // What `.Confirm(text, expect: …)` writes between the question and the answer it waits for: a control
    // character no question is written with, so one string carries both through the chain's own setter — and
    // with it the reset and the change fold every other step gets.
    internal const char ConfirmExpectSeparator = '\u001F';

    /// <summary>
    ///     Asks the reader before this element's click or submit is sent, in the browser's own dialog:
    ///     <c>Ui.Button.OnClick(() => Delete(row)).Confirm("Delete the partner?")</c>. Cancelled, nothing is sent —
    ///     not the event, and not the fields still waiting to go with it.
    /// </summary>
    /// <remarks>
    ///     Livewire's <c>wire:confirm</c>. <c>.Confirm("Type the name to delete it", expect: row.Name)</c> is its
    ///     <c>.prompt</c> form: the reader types the answer, and the action goes only when it matches exactly,
    ///     case and all. On a form it asks before the submit; on a submit button, before the submit it makes.
    /// </remarks>
    public string? Confirm
    {
        get => ConfirmInternal;
        set => ConfirmInternal = value;
    }

    // data-rask-confirm, and data-rask-confirm-expect for the prompt form: read by rask-confirm.ts before the
    // event leaves the browser.
    private void WriteConfirm(StringBuilder sb)
    {
        if (ConfirmInternal is not { } confirm)
        {
            return;
        }

        var split = confirm.IndexOf(ConfirmExpectSeparator, StringComparison.Ordinal);
        if (split < 0)
        {
            AppendAttr(sb, "data-", "rask-confirm", confirm);
            return;
        }

        AppendAttr(sb, "data-", "rask-confirm", confirm[..split]);
        AppendAttr(sb, "data-", "rask-confirm-expect", confirm[(split + 1)..]);
    }
}
