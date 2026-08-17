namespace Rask.Html.Components;

public sealed partial class Col : HtmlTableColElement
{
    protected override string TagName => "col";
    protected override bool SelfClosing => true;
}
