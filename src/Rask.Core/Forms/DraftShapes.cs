using System.Reflection;
using Rask.Values;

namespace Rask.Core.Forms;

// What a field over a value object of several values is told: a range held as it is draws both its ends under one
// label (its own `__Field`), and each end of its draft is of the ends' kind, within the range's bounds.
internal static class DraftShapes
{
    internal static FieldShape? Of(Type owner, PropertyInfo property)
    {
        if (ValueGroups.OfDraft(owner) is { } drafted)
        {
            return drafted.Shape is { Ends: { } kind } range && ValueFields.Of(property.PropertyType) is null
                ? new FieldShape(kind, Min: range.Min, Max: range.Max)
                : null;
        }

        return ValueGroups.Of(property.PropertyType)?.Shape;
    }
}
