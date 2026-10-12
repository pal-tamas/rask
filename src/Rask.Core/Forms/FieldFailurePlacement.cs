using Rask.Wire;

namespace Rask.Core.Forms;

// Puts what a submit handler threw, or what a store's rules found before it, onto the form: each failure's
// message under the bound fields it names, the rest of its fields marked. A failure about no field on this
// form goes where the form's own messages are drawn, and when nothing draws those it is handed back to be
// said in a toast.
internal static class FieldFailurePlacement
{
    /// <summary>Places every failure of a refused save it can, and hands back those nothing on the form shows.</summary>
    internal static List<FieldFailure> Place(EditContext form, IReadOnlyList<FieldFailure> failures) =>
        [.. failures.Where(failure => !Place(form, failure, null))];

    /// <summary>The same for what a check found before the save, remembered against the field whose commit asked.</summary>
    internal static void Place(EditContext form, IReadOnlyList<FieldFailure> failures, FieldIdentifier? checkedFor)
    {
        foreach (var failure in failures)
        {
            Place(form, failure, checkedFor);
        }
    }

    private static bool Place(EditContext form, FieldFailure failure, FieldIdentifier? checkedFor)
    {
        var under = Bound(form, failure.Fields ?? []);
        var marked = Bound(form, failure.Marked ?? []);
        if (under.Length == 0 && form.ShowsFormMessages)
        {
            under = [form.FormSlot];
        }

        form.AddFailure(failure.Message, under, marked, checkedFor);
        return under.Length > 0;
    }

    // A path is resolved against the model as it is now, so `Lines[2].ValidFrom` is the row at that index.
    private static FieldIdentifier[] Bound(EditContext form, IReadOnlyList<string> paths)
    {
        List<FieldIdentifier>? fields = null;
        foreach (var path in paths)
        {
            if (ModelGraphWalker.Resolve(form.Model, path) is var (owner, property)
                && new FieldIdentifier(owner, property) is var field
                && form.IsBound(field))
            {
                (fields ??= []).Add(field);
            }
        }

        return fields?.ToArray() ?? [];
    }
}
