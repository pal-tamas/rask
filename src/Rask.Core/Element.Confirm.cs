using System.Text;

namespace Rask.Core;

public abstract partial class Element
{
    // What `.Confirm(…)` asks (BuilderRuntime.Confirm): the question, and for the prompt form the answer it waits for.
    // Not a property: a chain step writes it, and the end-of-render reset drops it when a render stops naming it.
    internal void Ask(string text, string? expect)
    {
        ArgumentNullException.ThrowIfNull(text);
        var globals = GlobalAttrsForWrite;
        if (globals.Confirm is not { } current
            || !string.Equals(current.Text, text, StringComparison.Ordinal)
            || !string.Equals(current.Expect, expect, StringComparison.Ordinal))
        {
            globals.Confirm = current = new ConfirmAsk(text, expect);
            MarkEntryPropsChangedInternal();
        }

        current.Written = true;
    }

    // Called with the rest of the end-of-render reset (ResetUnwrittenSteps): a question no step wrote this render goes.
    internal void ResetUnwrittenConfirm()
    {
        if (GlobalAttrsInternal?.Confirm is not { } ask)
        {
            return;
        }

        if (ask.Written)
        {
            ask.Written = false;
            return;
        }

        GlobalAttrsInternal.Confirm = null;
        MarkEntryPropsChangedInternal();
    }

    // data-rask-confirm, and data-rask-confirm-expect for the prompt form: read by rask-confirm.ts before the
    // event leaves the browser.
    private void WriteConfirm(StringBuilder sb)
    {
        if (GlobalAttrsInternal?.Confirm is not { } ask)
        {
            return;
        }

        AppendAttr(sb, "data-", "rask-confirm", ask.Text);
        if (ask.Expect is { } expect)
        {
            AppendAttr(sb, "data-", "rask-confirm-expect", expect);
        }
    }
}
