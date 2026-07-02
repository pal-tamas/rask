namespace Rask.Example.EfCore.Shared.Forms;

// Shared ValidationMessage template — renders a field's inline messages under its input.
// Used by every slice's form, so it lives in the app-wide Shared/ folder rather than a slice.
public static class FieldErrors
{
    public static Component Template(IReadOnlyList<string> messages) =>
        [.. messages.Select((m, i) => Div(Key: i, Class: "text-danger small mt-1")[m])];
}
