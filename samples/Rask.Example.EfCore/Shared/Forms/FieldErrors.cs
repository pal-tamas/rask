namespace Rask.Example.EfCore.Shared.Forms;

// Shared ValidationMessage template — renders a field's inline messages under its input.
// Used by every slice's form, so it lives in the app-wide Shared/ folder rather than a slice.
//
// A render fragment is a DELEGATE, so this cannot be a component — and it used to be a `static class`,
// which cannot derive from anything and so could reach no builder entry. It is a markup host now: a
// sealed class that is never instantiated, which holds static members exactly as well as a static
// class did, and reaches `Div` because it derives from RaskMarkup.
public sealed partial class FieldErrors : RaskMarkup
{
    private FieldErrors()
    {
    }

    // `new` because `Template` is also the <template> tag's inherited entry — the collision the surface
    // creates the moment a type joins it, and the one RASK037's quick-fix resolves this same way.
    public static new Component Template(IReadOnlyList<string> messages) =>
        [.. messages.Select((m, i) => Div.Key(i).Class("text-danger small mt-1")[m])];
}
