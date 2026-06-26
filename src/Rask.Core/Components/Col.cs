namespace Rask.Core.Components;

public sealed class Col : HtmlTableColElement
{
    protected override string TagName => "col";
    protected override bool SelfClosing => true;
}
