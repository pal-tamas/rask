namespace Rask.Html.Components;

/// <summary>
///     Markup that is parsed but never rendered, to be cloned by script later. Rask builds its UI from
///     components, so this is only for interop with hand-written JS.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/template">MDN</see>
/// </summary>
public sealed partial class Template : Element
{
    protected override string TagName => "template";
}
