using System.Text;

namespace Rask.Html.Components;

/// <summary>
///     A native modal or non-modal dialog. Opened as a modal through the DOM's <c>showModal()</c>, it gets
///     a backdrop, a focus trap and top-layer stacking for free; setting <c>Open</c> alone shows it
///     non-modally, without any of that.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/dialog">MDN</see>
/// </summary>
public sealed partial class Dialog : Element
{
    protected override string TagName => "dialog";

    /// <summary>
    ///     Whether the dialog is shown. Shows it non-modally — it does <b>not</b> give you the backdrop and
    ///     focus trap that <c>showModal()</c> does.
    /// </summary>
    public bool? Open { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Open is true)
        {
            AppendAttr(sb, "open", null);
        }
    }
}
