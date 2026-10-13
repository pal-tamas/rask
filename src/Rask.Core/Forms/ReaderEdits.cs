using System.Runtime.CompilerServices;

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

    /// <summary>Notes that <paramref name="component" /> is drawn inside <paramref name="form" />.</summary>
    internal static void Hold(Component component, IGuardedForm form)
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
}
