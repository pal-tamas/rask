using System.Text;

namespace Rask.Html.Components;

/// <summary>
///     A caption for a form control. Point <c>For</c> at the control's <c>id</c> (or nest the control
///     inside) — that connection is what lets a click on the label focus the control and lets a screen
///     reader name it.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/label">MDN</see>
/// </summary>
public sealed partial class Label : Element
{
    protected override string TagName => "label";

    /// <summary>
    ///     The <c>id</c> of the control this labels. The label and the control must be in the same
    ///     document.
    /// </summary>
    public string? For { get; set; }

    /// <summary>The <c>id</c> of the form to associate with, for a label outside it.</summary>
    public string? Form { get; set; }

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
    }
}
