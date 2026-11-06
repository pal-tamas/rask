namespace Rask.Core.Forms;

// What a part drawn over the whole form (Ui.FormTabs) reads to point at what is wrong: which fields are
// invalid now, and how many submits the form has refused, so it can tell a refusal from a keystroke.
public sealed partial class EditContext
{
    /// <summary>
    ///     How many submits this form has refused: the rules failed, or the handler refused with field failures.
    ///     It only grows; a part that reads it compares it with the last value it saw.
    /// </summary>
    internal int Refusals
    {
        get
        {
            MarkReader();
            return _refusals;
        }
    }

    private int _refusals;

    /// <summary>Counts one refused submit (the form's submit bridge).</summary>
    internal void NoteRefused() => _refusals++;

    /// <summary>
    ///     Every field that is invalid now: it holds a message, or a failure whose message is under another
    ///     field marks it. The form's own slot (a message about the whole form) is no field and is left out.
    /// </summary>
    internal HashSet<FieldIdentifier> InvalidFields()
    {
        MarkReader();
        var invalid = new HashSet<FieldIdentifier>();
        foreach (var (field, state) in _states)
        {
            if (state.Messages.Count > 0 && field.FieldName.Length > 0)
            {
                invalid.Add(field);
            }
        }

        foreach (var failure in _failures ?? [])
        {
            invalid.UnionWith(failure.Marked);
        }

        return invalid;
    }
}
