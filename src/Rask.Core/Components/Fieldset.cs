using System.Text;

namespace Rask.Core.Components;

/// <summary>
///     Groups related controls in a form. Its first child should be a <c>legend</c>, which names the group
///     for screen readers.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/fieldset">MDN</see>
/// </summary>
public sealed class Fieldset : Element
{
    protected override string TagName => "fieldset";

    /// <summary>
    ///     Disables every control inside the group at once — except those in its <c>legend</c>.
    /// </summary>
    public bool? Disabled { get; set; }

    /// <summary>
    ///     The <c>id</c> of the form this group belongs to, for a fieldset that sits outside it.
    /// </summary>
    public new string? Form { get; set; }

    /// <summary>The group's name.</summary>
    public string? Name { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Disabled is true)
        {
            AppendAttr(sb, "disabled", null);
        }

        if (Form is not null)
        {
            AppendAttr(sb, "form", Form);
        }

        if (Name is not null)
        {
            AppendAttr(sb, "name", Name);
        }
    }
}
