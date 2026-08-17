namespace Rask.Html.Components;

/// <summary>
///     A line break inside text where the break is part of the content — a postal address, a line of verse.
///     Not a spacing tool: for space between blocks, use CSS margins.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/br">MDN</see>
/// </summary>
public sealed partial class Br : Element
{
    protected override string TagName => "br";
    protected override bool SelfClosing => true;
}
