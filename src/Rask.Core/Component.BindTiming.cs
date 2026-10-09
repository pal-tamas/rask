using System.Text;
using Rask.Core.Forms;
using Rask.Core.Live;

namespace Rask.Core;

public abstract partial class Component
{
    // A bound field that waits (`.Blur()`, `.Debounce(…)`). ONE handler writes the value and validates it, on
    // the event the step named: `change` for blur, `input` for a pause — which the runtime holds back for
    // `data-rask-debounce` milliseconds. `data-rask-bind-on` tells the morph the field is being typed into
    // although nothing streams from it. While a message shows, `data-rask-on-edit` asks to hear the first
    // keystroke, which is all C# hears of the typing: the message is about a value already being corrected.
    private protected static void WriteWaitingBind(
        StringBuilder sb, LiveRenderContext ctx, BindTiming timing, ExpressionAccessor.Accessor acc,
        EditContext? bindCtx, Func<Task>? afterBind)
    {
        var commit = ctx.RegisterHandler(
            BindingHelpers.TouchAndValidateHandler(acc, bindCtx, acc.Field, true, afterBind));
        if (timing.Pause > 0)
        {
            AppendAttr(sb, "data-rask-on-input", commit);
            AppendAttr(sb, "data-rask-debounce", timing.Pause);
        }
        else
        {
            AppendAttr(sb, "data-rask-on-change", commit);
            AppendAttr(sb, "data-rask-bind-on", "blur");
        }

        if (BindingHelpers.ClearOnEditHandler(bindCtx, acc.Field) is { } clear)
        {
            AppendAttr(sb, "data-rask-on-edit", ctx.RegisterHandler(clear));
        }
    }
}
