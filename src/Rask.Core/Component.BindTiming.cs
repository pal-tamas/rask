using System.Text;
using Rask.Core.Forms;
using Rask.Core.Live;

namespace Rask.Core;

public abstract partial class Component
{
    // A bound field that is typed into, wired for when it speaks (BindTiming). Unset it waits for the next
    // action, and `.Blur()` for the field to be left: both render ONE handler on `change`, and
    // `data-rask-bind-on` tells the runtime which of the two sends it. `.Live()` and `.Debounce(…)` render that
    // handler on `input`, held back in the browser for `data-rask-debounce` milliseconds. While a message
    // shows, `data-rask-on-edit` asks to hear the first keystroke, which is all C# hears of the typing: the
    // message is about a value already being corrected.
    private protected static void WriteTypedBind(
        StringBuilder sb, LiveRenderContext ctx, BindTiming timing, ExpressionAccessor.Accessor acc,
        EditContext? bindCtx, Func<Task>? afterBind, bool text)
    {
        if (timing.AtEveryKey && text)
        {
            WriteKeystrokeBind(sb, ctx, acc, bindCtx, afterBind);
            return;
        }

        var commit = ctx.RegisterHandler(
            BindingHelpers.TouchAndValidateHandler(acc, bindCtx, acc.Field, true, afterBind));
        if (timing.Pause > 0)
        {
            AppendAttr(sb, "data-rask-on-input", commit);
            AppendAttr(sb, "data-rask-debounce", timing.Pause);
        }
        else if (timing.AtEveryKey)
        {
            AppendAttr(sb, "data-rask-on-input", commit);
        }
        else
        {
            AppendAttr(sb, "data-rask-on-change", commit);
            AppendAttr(sb, "data-rask-bind-on", timing.WaitsForAction ? "action" : "blur");
        }

        if (BindingHelpers.ClearOnEditHandler(bindCtx, acc.Field) is { } clear)
        {
            AppendAttr(sb, "data-rask-on-edit", ctx.RegisterHandler(clear));
        }
    }

    // Text sent at every key (`.Debounce(TimeSpan.Zero)`): the model is written on `input`, and the field is
    // touched and its rules run on `change`, as it is left. Once touched, each key runs them again.
    private protected static void WriteKeystrokeBind(
        StringBuilder sb, LiveRenderContext ctx, ExpressionAccessor.Accessor acc, EditContext? bindCtx,
        Func<Task>? afterBind)
    {
        AppendAttr(sb, "data-rask-on-input",
            ctx.RegisterHandler(BindingHelpers.StringSetHandler(acc, bindCtx, acc.Field, false, afterBind)));
        AppendAttr(sb, "data-rask-on-change",
            ctx.RegisterHandler(BindingHelpers.TouchAndValidateHandler(acc, bindCtx, acc.Field, false)));
    }

    // A bound control that is CHOSEN rather than typed into — a checkbox, a radio, a select, a range: one
    // handler on `change`. Unset it waits for the next action like any bound control, and the runtime keeps
    // the choice until then; `.Live()` sends it as it is made. A message under one that waits goes when the
    // reader chooses again, as it does under a field at the first keystroke.
    private protected static void WriteChosenBind(
        StringBuilder sb, LiveRenderContext ctx, bool live, Delegate commit, EditContext? bindCtx, FieldIdentifier field)
    {
        AppendAttr(sb, "data-rask-on-change", ctx.RegisterHandler(commit));
        if (live)
        {
            return;
        }

        AppendAttr(sb, "data-rask-bind-on", "action");
        if (BindingHelpers.ClearOnEditHandler(bindCtx, field) is { } clear)
        {
            AppendAttr(sb, "data-rask-on-edit", ctx.RegisterHandler(clear));
        }
    }
}
