using System.Reflection;
using Rask.Wire;

namespace Rask.Core.Forms;

// Puts what a submit handler threw, or what a store's rules found before it, onto the form: each failure's
// message under the bound fields it names, the rest of its fields marked. A failure about no field on this
// form goes where the form's own messages are drawn, and when nothing draws those it is left for the form's
// error.
internal static class FieldFailurePlacement
{
    /// <summary>The field failures <paramref name="thrown" /> carries, looking through the wrappers a call adds.</summary>
    internal static IFieldFailures? Find(Exception thrown) => thrown switch
    {
        IFieldFailures failures => failures,
        AggregateException many => many.Flatten().InnerExceptions.Select(Find).FirstOrDefault(found => found is not null),
        TargetInvocationException { InnerException: { } inner } => Find(inner),
        _ => null,
    };

    /// <summary>Places every failure it can, and says whether each one has somewhere its message is read.</summary>
    // An exception that names no failure has told the reader nothing, so it is not placed: it stays a fault.
    internal static bool Place(EditContext form, IFieldFailures failures) =>
        failures.Failures.Count > 0 && Place(form, failures.Failures, null);

    /// <summary>The same for what a check found before the save, remembered against the field whose commit asked.</summary>
    internal static bool Place(EditContext form, IReadOnlyList<FieldFailure> failures, FieldIdentifier? checkedFor)
    {
        var allShown = true;
        foreach (var failure in failures)
        {
            allShown &= Place(form, failure, checkedFor);
        }

        return allShown;
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
