namespace Rask.Example.EfCore.Shared.Forms;

// Shared ValidationMessage template — renders a field's inline messages under its input.
// Used by every slice's form, so it lives in the app-wide Shared/ folder rather than a slice.
//
// A render fragment is a DELEGATE, so this cannot be a component — and a `static class` can derive from
// nothing, so it cannot reach a builder entry by inheriting one either. [RaskMarkup] is the way in for
// exactly that: it stays static, and the framework entries are injected as members of this type.
//
// `Template` needs no `new` here, and could not use one: an injected entry of that name would be a
// SECOND member of this type (CS0102), not an inherited one to hide. So the name stays with the member
// written below and the <template> entry is simply not injected — same outcome as `new`, no ceremony.
[RaskMarkup]
public static partial class FieldErrors
{
    public static Component Template(IReadOnlyList<string> messages) =>
        [.. messages.Select((m, i) => Div.Key(i).Class("text-danger text-sm mt-1")[m])];
}
