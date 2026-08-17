namespace Rask.Html.Components;

/// <summary>
///     An optional word-break opportunity: the browser may break the line here if it needs to, and will not
///     otherwise.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/wbr">MDN</see>
/// </summary>
public sealed partial class Wbr : Element
{
    protected override string TagName => "wbr";
    protected override bool SelfClosing => true;
}
