using System.Text;

namespace Rask.Core.Components;

/// <summary>
///     The result of a calculation or user action. It is a live region, so screen readers announce changes
///     to it without any extra ARIA.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/output">MDN</see>
/// </summary>
public sealed class Output : Element
{
    protected override string TagName => "output";

    /// <summary>Space-separated <c>id</c>s of the elements this result was computed from.</summary>
    public string? For { get; set; }

    /// <summary>The <c>id</c> of the form this output belongs to.</summary>
    public new string? Form { get; set; }

    /// <summary>The control's name, used when the form is submitted.</summary>
    public string? Name { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (For is not null)
        {
            AppendAttr(sb, "for", For);
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
