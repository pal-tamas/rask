namespace Rask.Core.Components;

/// <summary>
///     Markup that is parsed but never rendered, to be cloned by script later. Rask builds its UI from
///     components, so this is only for interop with hand-written JS.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/template">MDN</see>
/// </summary>
[Tag("template")]
public sealed partial class Template : Element
{
}
