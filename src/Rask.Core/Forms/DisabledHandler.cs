using Rask.Core.Diagnostics;
using Rask.Core.Live;

namespace Rask.Core.Forms;

// A handler a disabled form control registered (#1273). The browser sends no event to a button, an input, a select
// or a textarea drawn disabled — itself, or inside a <fieldset disabled> — so whatever arrives for one was written by
// hand. It is refused here, once for every handler of every such control, where its handlers are registered
// (LiveRenderContext.RegisterHandler): nothing runs, nothing is written, and Rask.Forms says so once at Warning,
// naming the control and never what it was sent. The markup is the same as an enabled control's.
//
// Held in the shape of the handler it stands for, so a frame of the kind the control is sent — a value, a selection,
// files — still reaches it and is refused, not dropped as a stale id. A DOM event handler (a click) becomes a
// parameterless one, which every DOM event feeds. An adapter (IHandlerAdapter): the owner, and whether a later
// render's handler is the same one, are read from the handler as written.
internal sealed class DisabledHandler(Delegate inner, IFormControlElement control, Component drawnBy) : IHandlerAdapter
{
    public Delegate Inner => inner;

    /// <summary>The handler, refused: what <paramref name="control" /> would run while enabled never runs.</summary>
    /// <param name="handler">The handler the control registered.</param>
    /// <param name="control">The disabled control that registered it.</param>
    /// <param name="drawnBy">The component whose subtree it is drawn in, which may name it (<see cref="INamesDrawnControls" />).</param>
    internal static Delegate For(Delegate handler, IFormControlElement control, Component drawnBy)
    {
        var refused = new DisabledHandler(handler, control, drawnBy);
        return HandlerFrameShape.ShapeOf(handler) switch
        {
            HandlerFrameShape.Shape.Value => new Func<string, Task>(refused.RefuseOne),
            HandlerFrameShape.Shape.Values => new Func<IReadOnlyList<string>, Task>(refused.RefuseOne),
            HandlerFrameShape.Shape.Form => new Func<FormData, Task>(refused.RefuseOne),
            HandlerFrameShape.Shape.Files => new Func<IReadOnlyList<IRaskFile>, Task>(refused.RefuseOne),
            _ => new Action(refused.Refuse),
        };
    }

    /// <summary>Says once, at Warning, that a control drawn disabled was sent an event — never what it carried.</summary>
    /// <param name="control">What the control is called in the log: <c>input</c>, <c>Ui.Slider</c>.</param>
    /// <param name="name">The name it posts under, or null.</param>
    internal static void Say(string control, string? name) =>
        RaskDiagnostics.Report(
            RaskLogLevel.Warning, "Rask.Forms",
            $"Rask forms: the {control} '{name ?? "(unnamed)"}' is drawn disabled, so the browser sends it no event, but one arrived; it was refused, nothing ran and the field keeps its value. See https://rask.sh/docs/guides/forms#a-choice-takes-only-what-it-offered");

    private Task RefuseOne<T>(T sent)
    {
        Refuse();
        return Task.CompletedTask;
    }

    private void Refuse()
    {
        if (drawnBy is INamesDrawnControls named)
        {
            Say(named.Called, named.PostsUnder ?? control.PostsUnder);
            return;
        }

        Say(control.Control, control.PostsUnder);
    }
}
