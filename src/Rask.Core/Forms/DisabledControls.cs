namespace Rask.Core.Forms;

// The elements HTML disables, and so the ones whose handlers Rask refuses while they are drawn disabled — themselves,
// or inside a <fieldset disabled> outside its first <legend> (#1273). One list for every walk that judges it: Rask's
// own serializer reads it through the typed elements (IFormControlElement.Control), and a walk over markup Rask did
// not build, a hosted Blazor component's render frames (#1274), reads it by tag name.
internal static class DisabledControls
{
    internal const string Button = "button";
    internal const string Input = "input";
    internal const string Select = "select";
    internal const string Textarea = "textarea";
    internal const string Fieldset = "fieldset";
    internal const string Legend = "legend";

    /// <summary>Whether <paramref name="tagName" /> is a control the browser sends no event to while it is disabled.</summary>
    internal static bool IsControl(string tagName) =>
        Is(tagName, Button) || Is(tagName, Input) || Is(tagName, Select) || Is(tagName, Textarea);

    /// <summary>Whether <paramref name="tagName" /> is <paramref name="tag" />, as HTML reads a tag: case aside.</summary>
    internal static bool Is(string tagName, string tag) => string.Equals(tagName, tag, StringComparison.OrdinalIgnoreCase);
}
