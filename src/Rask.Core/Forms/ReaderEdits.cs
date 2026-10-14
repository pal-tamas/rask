using System.Runtime.CompilerServices;
using Rask.Core.Live;

namespace Rask.Core.Forms;

// What the reader changed in a control that draws itself in C#.
//
// A form that asks before it is left (`ConfirmLeave`) counts its edits in the browser, from `input` and
// `change` (rask-leave.ts). A listbox, a calendar or a colour sheet raises neither: its pick is a press the
// page hears, because the page draws the list. So the page says it: the form the control is drawn in is
// told, and carries `data-rask-edited` until a submit it accepts has carried the value.
//
// WHICH FORM. The one whose <form> the control is drawn inside, whatever it binds to and whoever wrote it
// there: the walk notes every component it meets while a guarded form's children are being written
// (HtmlSerializer.AttachToLiveTree). Weakly, so a control that has gone costs nothing.
//
// WHO SAYS IT. The kit's commit — the one place a control hands over what the reader chose (UiFormCommit) —
// written to the model yet or not. A value the page gives a control goes through no commit, and marks nothing.
internal static class ReaderEdits
{
    private static readonly ConditionalWeakTable<Component, IGuardedForm> _formOf = [];

    // The guarded form whose children a walk on this thread is writing, with that walk, so a walk begun inside
    // it (another root's) is in no form. Not a field of LiveRenderContext: every render of every app allocates
    // one of those, and only a ConfirmLeave form needs this. The walk is synchronous, so one thread sees it all.
    [ThreadStatic] private static Writing? _writing;

    /// <summary>Opens <paramref name="form" />'s children to <paramref name="live" />'s walk; disposing closes them.</summary>
    internal static IDisposable Write(LiveRenderContext live, IGuardedForm form, IDisposable? bound) => _writing = new Writing(live, form, bound, _writing);

    /// <summary>Notes that <paramref name="component" />, met by <paramref name="live" />'s walk, is drawn inside the form being written.</summary>
    internal static void HoldIfWritten(Component component, LiveRenderContext live)
    {
        if (_writing is { } writing && ReferenceEquals(writing.Live, live))
        {
            Hold(component, writing.Form);
        }
    }

    private static void Hold(Component component, IGuardedForm form)
    {
        if (!_formOf.TryGetValue(component, out var held) || !ReferenceEquals(held, form))
        {
            _formOf.AddOrUpdate(component, form);
        }
    }

    /// <summary>Tells the guarded form <paramref name="control" /> is drawn in that the reader changed its value.</summary>
    internal static void Note(Component control)
    {
        if (_formOf.TryGetValue(control, out var form))
        {
            form.NoteReaderEdit();
        }
    }

    private sealed class Writing(LiveRenderContext live, IGuardedForm form, IDisposable? bound, Writing? outer) : IDisposable
    {
        internal LiveRenderContext Live => live;

        internal IGuardedForm Form => form;

        public void Dispose()
        {
            Close(outer);
            bound?.Dispose();
        }

        private static void Close(Writing? outer) => _writing = outer;
    }
}
