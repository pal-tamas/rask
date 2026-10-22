using System.Text;

namespace Rask.Core;

public abstract partial class Element
{
    // What `.Confirm(…)` asks (BuilderRuntime.Confirm): the question, and for the prompt form the answer it waits for.
    // Not a property: a chain step writes it, and the end-of-render reset drops it when a render stops naming it.
    internal void Ask(ConfirmAsk ask)
    {
        var globals = GlobalAttrsForWrite;
        if (globals.Confirm != ask)
        {
            globals.Confirm = ask;
            MarkEntryPropsChangedInternal();
        }

        globals.ConfirmWritten = true;
    }

    // Called with the rest of the end-of-render reset (ResetUnwrittenSteps): a question no step wrote this render goes.
    internal void ResetUnwrittenConfirm()
    {
        if (GlobalAttrsInternal is not { } globals)
        {
            return;
        }

        if (!globals.ConfirmWritten && globals.Confirm is not null)
        {
            globals.Confirm = null;
            MarkEntryPropsChangedInternal();
        }

        globals.ConfirmWritten = false;
    }

    // data-rask-confirm, data-rask-confirm-expect for the prompt form and data-rask-confirm-heading for Ui.Confirm's
    // title: read by rask-confirm.ts before the event leaves the browser.
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

        if (ask.Heading is { } heading)
        {
            AppendAttr(sb, "data-", "rask-confirm-heading", heading);
        }
    }
}
